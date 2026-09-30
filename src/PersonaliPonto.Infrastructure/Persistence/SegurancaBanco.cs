namespace PersonaliPonto.Infrastructure.Persistence;

/// <summary>
/// SQL de segurança aplicado pelas migrations: papel da aplicação sem BYPASSRLS, Row-Level Security
/// forçada em toda tabela com tenant_id e bloqueio físico de UPDATE/DELETE/TRUNCATE nas tabelas imutáveis.
/// </summary>
public static class SegurancaBanco
{
    /// <summary>Tabelas cujo conteúdo nunca pode ser alterado ou excluído.</summary>
    public static readonly string[] TabelasImutaveis =
        ["registros_rep", "marcacoes_contexto", "tratamentos", "audit_logs", "banco_horas", "assinaturas_espelho"];

    public const string Funcoes = """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'personaliponto_app') THEN
                CREATE ROLE personaliponto_app NOLOGIN NOBYPASSRLS;
            END IF;
        END $$;

        GRANT personaliponto_app TO CURRENT_USER;
        GRANT USAGE ON SCHEMA personaliponto TO personaliponto_app;

        -- O schema da aplicação nunca é exposto pela Data API do Supabase.
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
                EXECUTE 'REVOKE ALL ON SCHEMA personaliponto FROM anon';
            END IF;
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
                EXECUTE 'REVOKE ALL ON SCHEMA personaliponto FROM authenticated';
            END IF;
        END $$;

        CREATE OR REPLACE FUNCTION personaliponto.tenant_atual() RETURNS uuid
            LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid $f$;

        CREATE OR REPLACE FUNCTION personaliponto.acesso_sistema() RETURNS boolean
            LANGUAGE sql STABLE AS
            $f$ SELECT coalesce(current_setting('app.bypass_rls', true), 'off') = 'on' $f$;

        CREATE OR REPLACE FUNCTION personaliponto.bloquear_alteracao() RETURNS trigger
            LANGUAGE plpgsql AS
            $f$
            BEGIN
                RAISE EXCEPTION 'Tabela % é imutável: % não permitido (Portaria MTP 671/2021, Anexo IX, item 7).', TG_TABLE_NAME, TG_OP
                    USING ERRCODE = 'insufficient_privilege';
            END;
            $f$;

        -- Habilita e força RLS em toda tabela do schema com coluna tenant_id (e na tabela tenants).
        CREATE OR REPLACE FUNCTION personaliponto.aplicar_rls() RETURNS void
            LANGUAGE plpgsql AS
            $f$
            DECLARE t record;
            BEGIN
                FOR t IN
                    SELECT c.relname AS tabela,
                           EXISTS (SELECT 1 FROM information_schema.columns col
                                   WHERE col.table_schema = 'personaliponto' AND col.table_name = c.relname AND col.column_name = 'tenant_id') AS tem_tenant
                    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'personaliponto' AND c.relkind = 'r' AND c.relname <> '__EFMigrationsHistory'
                LOOP
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON personaliponto.%I TO personaliponto_app', t.tabela);
                    IF t.tabela = 'tenants' THEN
                        EXECUTE 'ALTER TABLE personaliponto.tenants ENABLE ROW LEVEL SECURITY';
                        EXECUTE 'ALTER TABLE personaliponto.tenants FORCE ROW LEVEL SECURITY';
                        EXECUTE 'DROP POLICY IF EXISTS isolamento_tenant ON personaliponto.tenants';
                        EXECUTE 'CREATE POLICY isolamento_tenant ON personaliponto.tenants USING (personaliponto.acesso_sistema() OR id = personaliponto.tenant_atual()) WITH CHECK (personaliponto.acesso_sistema() OR id = personaliponto.tenant_atual())';
                    ELSIF t.tem_tenant THEN
                        EXECUTE format('ALTER TABLE personaliponto.%I ENABLE ROW LEVEL SECURITY', t.tabela);
                        EXECUTE format('ALTER TABLE personaliponto.%I FORCE ROW LEVEL SECURITY', t.tabela);
                        EXECUTE format('DROP POLICY IF EXISTS isolamento_tenant ON personaliponto.%I', t.tabela);
                        EXECUTE format('CREATE POLICY isolamento_tenant ON personaliponto.%I USING (personaliponto.acesso_sistema() OR tenant_id = personaliponto.tenant_atual()) WITH CHECK (personaliponto.acesso_sistema() OR tenant_id = personaliponto.tenant_atual())', t.tabela);
                    ELSE
                        -- Tabelas globais (ex.: planos): leitura para todos, escrita só em acesso de sistema.
                        EXECUTE format('ALTER TABLE personaliponto.%I ENABLE ROW LEVEL SECURITY', t.tabela);
                        EXECUTE format('ALTER TABLE personaliponto.%I FORCE ROW LEVEL SECURITY', t.tabela);
                        EXECUTE format('DROP POLICY IF EXISTS leitura_global ON personaliponto.%I', t.tabela);
                        EXECUTE format('DROP POLICY IF EXISTS escrita_sistema ON personaliponto.%I', t.tabela);
                        EXECUTE format('CREATE POLICY leitura_global ON personaliponto.%I FOR SELECT USING (true)', t.tabela);
                        EXECUTE format('CREATE POLICY escrita_sistema ON personaliponto.%I FOR ALL USING (personaliponto.acesso_sistema()) WITH CHECK (personaliponto.acesso_sistema())', t.tabela);
                    END IF;
                END LOOP;
                EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA personaliponto TO personaliponto_app';
            END;
            $f$;
        """;

    public static string Imutabilidade()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in TabelasImutaveis)
        {
            sb.AppendLine($"DROP TRIGGER IF EXISTS tg_imutavel ON personaliponto.{t};");
            sb.AppendLine($"CREATE TRIGGER tg_imutavel BEFORE UPDATE OR DELETE ON personaliponto.{t} FOR EACH ROW EXECUTE FUNCTION personaliponto.bloquear_alteracao();");
            sb.AppendLine($"DROP TRIGGER IF EXISTS tg_imutavel_truncate ON personaliponto.{t};");
            sb.AppendLine($"CREATE TRIGGER tg_imutavel_truncate BEFORE TRUNCATE ON personaliponto.{t} FOR EACH STATEMENT EXECUTE FUNCTION personaliponto.bloquear_alteracao();");
        }
        return sb.ToString();
    }

    public const string Aplicar = "SELECT personaliponto.aplicar_rls();";

    /// <summary>
    /// Tabelas de cadastro/financeiro do cliente visíveis ao canal dono e aos canais ascendentes
    /// (além da própria tabela tenants). Demais tabelas com tenant_id (ponto/RH) seguem só do tenant:
    /// o canal as acessa apenas em modo suporte auditado. Deve coincidir com
    /// <see cref="PersonaliPontoDbContext.EntidadesCarteiraCanal"/>.
    /// </summary>
    public static readonly string[] TabelasCarteiraCanal = ["assinaturas", "faturas"];

    /// <summary>
    /// RLS v2 (etapa E1 — canais). Variáveis de sessão adicionais aplicadas pelo <see cref="TenantSessionInterceptor"/>:
    /// app.canal_id, app.canais_canal (uuid[] do canal e descendentes) e app.tenants_canal (uuid[] da carteira).
    /// Políticas:
    /// <list type="bullet">
    /// <item>tenants: sistema OR id = tenant_atual() OR id = ANY(tenants_canal()); inserção/alteração pelo canal
    /// exige canal_dono_id visível.</item>
    /// <item>carteira (assinaturas, faturas): sistema OR tenant_id = tenant_atual() OR tenant_id = ANY(tenants_canal()).</item>
    /// <item>tabelas com tenant_id e canal_id (usuarios, audit_logs, acessos_suporte): sistema OR tenant_id = tenant_atual()
    /// OR canal_id = ANY(canais_canal()).</item>
    /// <item>demais tabelas com tenant_id (ponto/RH): sistema OR tenant_id = tenant_atual().</item>
    /// <item>canais: sistema OR id = ANY(canais_canal()); criação só como filho de canal visível.</item>
    /// <item>tabelas com canal_id sem tenant_id: sistema OR canal_id = ANY(canais_canal()).</item>
    /// <item>globais (planos, municipios): leitura livre, escrita só sistema.</item>
    /// </list>
    /// </summary>
    public static string FuncoesCanal => $$"""
        CREATE OR REPLACE FUNCTION personaliponto.canal_atual() RETURNS uuid
            LANGUAGE sql STABLE AS
            $f$ SELECT NULLIF(current_setting('app.canal_id', true), '')::uuid $f$;

        CREATE OR REPLACE FUNCTION personaliponto.canais_canal() RETURNS uuid[]
            LANGUAGE sql STABLE AS
            $f$ SELECT coalesce(NULLIF(current_setting('app.canais_canal', true), '')::uuid[], '{}'::uuid[]) $f$;

        CREATE OR REPLACE FUNCTION personaliponto.tenants_canal() RETURNS uuid[]
            LANGUAGE sql STABLE AS
            $f$ SELECT coalesce(NULLIF(current_setting('app.tenants_canal', true), '')::uuid[], '{}'::uuid[]) $f$;

        -- Hierarquia de canais: nível e caminho materializado derivados do pai; no máximo 3 níveis;
        -- pai/tipo imutáveis; situação comercial só alterada pela plataforma ou por um canal ascendente.
        CREATE OR REPLACE FUNCTION personaliponto.canais_hierarquia() RETURNS trigger
            LANGUAGE plpgsql AS
            $f$
            DECLARE pai record;
            BEGIN
                IF TG_OP = 'UPDATE' THEN
                    IF NEW.canal_pai_id IS DISTINCT FROM OLD.canal_pai_id OR NEW.tipo <> OLD.tipo OR NEW.id <> OLD.id THEN
                        RAISE EXCEPTION 'A posição de um canal na hierarquia não pode ser alterada.' USING ERRCODE = 'check_violation';
                    END IF;
                    NEW.caminho := OLD.caminho;
                    NEW.nivel := OLD.nivel;
                    IF NEW.status <> OLD.status AND NOT (personaliponto.acesso_sistema()
                        OR (OLD.canal_pai_id IS NOT NULL AND OLD.canal_pai_id = ANY(personaliponto.canais_canal()))) THEN
                        RAISE EXCEPTION 'Somente um canal ascendente pode alterar a situação do canal.' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END IF;
                IF NEW.canal_pai_id IS NULL THEN
                    NEW.nivel := 1;
                    NEW.caminho := '/' || NEW.id::text || '/';
                ELSE
                    SELECT c.nivel, c.caminho INTO pai FROM personaliponto.canais c WHERE c.id = NEW.canal_pai_id;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Canal pai inexistente ou fora da hierarquia.' USING ERRCODE = 'foreign_key_violation';
                    END IF;
                    NEW.nivel := pai.nivel + 1;
                    NEW.caminho := pai.caminho || NEW.id::text || '/';
                END IF;
                IF NEW.nivel > 3 THEN
                    RAISE EXCEPTION 'Profundidade máxima de canais excedida (Owner > Revendedor > Parceiro).' USING ERRCODE = 'check_violation';
                END IF;
                RETURN NEW;
            END;
            $f$;

        DROP TRIGGER IF EXISTS tg_canais_hierarquia ON personaliponto.canais;
        CREATE TRIGGER tg_canais_hierarquia BEFORE INSERT OR UPDATE ON personaliponto.canais
            FOR EACH ROW EXECUTE FUNCTION personaliponto.canais_hierarquia();

        -- Transferência de cliente entre canais: só a plataforma ou um canal que enxergue origem e destino.
        CREATE OR REPLACE FUNCTION personaliponto.tenants_canal_dono() RETURNS trigger
            LANGUAGE plpgsql AS
            $f$
            BEGIN
                IF NEW.canal_dono_id IS DISTINCT FROM OLD.canal_dono_id AND NOT (personaliponto.acesso_sistema()
                    OR (OLD.canal_dono_id = ANY(personaliponto.canais_canal()) AND NEW.canal_dono_id = ANY(personaliponto.canais_canal()))) THEN
                    RAISE EXCEPTION 'Transferência de cliente entre canais não permitida.' USING ERRCODE = 'insufficient_privilege';
                END IF;
                RETURN NEW;
            END;
            $f$;

        DROP TRIGGER IF EXISTS tg_tenants_canal_dono ON personaliponto.tenants;
        CREATE TRIGGER tg_tenants_canal_dono BEFORE UPDATE ON personaliponto.tenants
            FOR EACH ROW EXECUTE FUNCTION personaliponto.tenants_canal_dono();

        CREATE OR REPLACE FUNCTION personaliponto.aplicar_rls() RETURNS void
            LANGUAGE plpgsql AS
            $f$
            DECLARE
                t record;
                sis text := 'personaliponto.acesso_sistema()';
                pol text;
                chk text;
            BEGIN
                FOR t IN
                    SELECT c.relname AS tabela,
                           EXISTS (SELECT 1 FROM information_schema.columns col
                                   WHERE col.table_schema = 'personaliponto' AND col.table_name = c.relname AND col.column_name = 'tenant_id') AS tem_tenant,
                           EXISTS (SELECT 1 FROM information_schema.columns col
                                   WHERE col.table_schema = 'personaliponto' AND col.table_name = c.relname AND col.column_name = 'canal_id') AS tem_canal
                    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'personaliponto' AND c.relkind = 'r' AND c.relname <> '__EFMigrationsHistory'
                LOOP
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON personaliponto.%I TO personaliponto_app', t.tabela);
                    EXECUTE format('ALTER TABLE personaliponto.%I ENABLE ROW LEVEL SECURITY', t.tabela);
                    EXECUTE format('ALTER TABLE personaliponto.%I FORCE ROW LEVEL SECURITY', t.tabela);
                    EXECUTE format('DROP POLICY IF EXISTS isolamento_tenant ON personaliponto.%I', t.tabela);
                    EXECUTE format('DROP POLICY IF EXISTS isolamento_canal ON personaliponto.%I', t.tabela);
                    EXECUTE format('DROP POLICY IF EXISTS leitura_global ON personaliponto.%I', t.tabela);
                    EXECUTE format('DROP POLICY IF EXISTS escrita_sistema ON personaliponto.%I', t.tabela);

                    pol := NULL;
                    chk := NULL;
                    IF t.tabela = 'tenants' THEN
                        pol := sis || ' OR id = personaliponto.tenant_atual() OR id = ANY(personaliponto.tenants_canal())';
                        chk := sis || ' OR id = personaliponto.tenant_atual() OR canal_dono_id = ANY(personaliponto.canais_canal())';
                    ELSIF t.tabela = 'canais' THEN
                        pol := sis || ' OR id = ANY(personaliponto.canais_canal())';
                        chk := sis || ' OR id = ANY(personaliponto.canais_canal()) OR canal_pai_id = ANY(personaliponto.canais_canal())';
                    ELSIF t.tem_tenant AND t.tabela = ANY(ARRAY[{{string.Join(", ", TabelasCarteiraCanal.Select(x => "'" + x + "'"))}}]) THEN
                        pol := sis || ' OR tenant_id = personaliponto.tenant_atual() OR tenant_id = ANY(personaliponto.tenants_canal())';
                    ELSIF t.tem_tenant AND t.tem_canal THEN
                        pol := sis || ' OR tenant_id = personaliponto.tenant_atual() OR canal_id = ANY(personaliponto.canais_canal())';
                    ELSIF t.tem_tenant THEN
                        pol := sis || ' OR tenant_id = personaliponto.tenant_atual()';
                    ELSIF t.tem_canal THEN
                        pol := sis || ' OR canal_id = ANY(personaliponto.canais_canal())';
                    END IF;

                    IF pol IS NOT NULL THEN
                        EXECUTE format('CREATE POLICY %I ON personaliponto.%I USING (%s) WITH CHECK (%s)',
                            CASE WHEN t.tem_tenant OR t.tabela = 'tenants' THEN 'isolamento_tenant' ELSE 'isolamento_canal' END,
                            t.tabela, pol, coalesce(chk, pol));
                    ELSE
                        -- Tabelas globais (planos, municípios): leitura para todos, escrita só em acesso de sistema.
                        EXECUTE format('CREATE POLICY leitura_global ON personaliponto.%I FOR SELECT USING (true)', t.tabela);
                        EXECUTE format('CREATE POLICY escrita_sistema ON personaliponto.%I FOR ALL USING (%s) WITH CHECK (%s)', t.tabela, sis, sis);
                    END IF;
                END LOOP;
                EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA personaliponto TO personaliponto_app';
            END;
            $f$;
        """;

    /// <summary>Remove todas as políticas RLS do schema (usado antes de reaplicar uma versão anterior no Down).</summary>
    public const string RemoverPoliticas = """
        DO $$
        DECLARE p record;
        BEGIN
            FOR p IN SELECT policyname, tablename FROM pg_policies WHERE schemaname = 'personaliponto' LOOP
                EXECUTE format('DROP POLICY IF EXISTS %I ON personaliponto.%I', p.policyname, p.tablename);
            END LOOP;
        END $$;
        """;

    /// <summary>Tabelas de apuração de uso de canal (snapshot imutável, E2).</summary>
    public static readonly string[] TabelasImutaveisCanal = ["apuracoes_uso_canal", "apuracoes_uso_tenant"];

    public static string ImutabilidadeCanal()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in TabelasImutaveisCanal)
        {
            sb.AppendLine($"DROP TRIGGER IF EXISTS tg_imutavel ON personaliponto.{t};");
            sb.AppendLine($"CREATE TRIGGER tg_imutavel BEFORE UPDATE OR DELETE ON personaliponto.{t} FOR EACH ROW EXECUTE FUNCTION personaliponto.bloquear_alteracao();");
            sb.AppendLine($"DROP TRIGGER IF EXISTS tg_imutavel_truncate ON personaliponto.{t};");
            sb.AppendLine($"CREATE TRIGGER tg_imutavel_truncate BEFORE TRUNCATE ON personaliponto.{t} FOR EACH STATEMENT EXECUTE FUNCTION personaliponto.bloquear_alteracao();");
        }
        return sb.ToString();
    }

    /// <summary>
    /// RLS v3 (etapa E2 — faturamento de canais + defesa em profundidade por comando). Políticas separadas por
    /// comando (SELECT/INSERT/UPDATE/DELETE); fora do modo sistema:
    /// <list type="bullet">
    /// <item>carteira (assinaturas, faturas): lê/insere tenant ou carteira do canal; UPDATE só pelo canal (o cliente não
    /// altera o próprio financeiro); DELETE só sistema.</item>
    /// <item>audit_logs: só SELECT/INSERT. acessos_suporte: só SELECT/INSERT e visível apenas ao canal de origem
    /// (o cliente acessado não lê o registro; acessos da plataforma têm canal de origem = Owner).</item>
    /// <item>tenants/canais: DELETE só sistema.</item>
    /// <item>faturas_canal: visível ao pagador (e ascendentes dele que o vejam na hierarquia) e ao recebedor; INSERT só
    /// sistema; UPDATE só pelo recebedor e apenas campos de baixa (trigger); DELETE só sistema. Itens seguem a fatura
    /// e só o sistema grava.</item>
    /// <item>apurações: leitura pelo revendedor apurado; escrita só sistema (e imutáveis).</item>
    /// <item>assinaturas_premium: leitura pela hierarquia; INSERT/UPDATE só pelo próprio canal; DELETE só sistema.</item>
    /// </list>
    /// Condições comerciais do canal (isenção Premium, app próprio) só pela plataforma; preço do parceiro só por
    /// ascendente (trigger canais_hierarquia v3).
    /// </summary>
    public static string FuncoesCanalV3 => $$"""
        CREATE OR REPLACE FUNCTION personaliponto.canais_hierarquia() RETURNS trigger
            LANGUAGE plpgsql AS
            $f$
            DECLARE pai record;
            BEGIN
                IF TG_OP = 'UPDATE' THEN
                    IF NEW.canal_pai_id IS DISTINCT FROM OLD.canal_pai_id OR NEW.tipo <> OLD.tipo OR NEW.id <> OLD.id THEN
                        RAISE EXCEPTION 'A posição de um canal na hierarquia não pode ser alterada.' USING ERRCODE = 'check_violation';
                    END IF;
                    NEW.caminho := OLD.caminho;
                    NEW.nivel := OLD.nivel;
                    IF NEW.status <> OLD.status AND NOT (personaliponto.acesso_sistema()
                        OR (OLD.canal_pai_id IS NOT NULL AND OLD.canal_pai_id = ANY(personaliponto.canais_canal()))) THEN
                        RAISE EXCEPTION 'Somente um canal ascendente pode alterar a situação do canal.' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF NOT personaliponto.acesso_sistema() AND (NEW.premium_isento_ate IS DISTINCT FROM OLD.premium_isento_ate
                        OR NEW.app_proprio_ativo IS DISTINCT FROM OLD.app_proprio_ativo OR NEW.app_proprio_desde IS DISTINCT FROM OLD.app_proprio_desde
                        OR NEW.app_proprio_implantacao_cobrada IS DISTINCT FROM OLD.app_proprio_implantacao_cobrada) THEN
                        RAISE EXCEPTION 'Condições comerciais do canal são definidas pela plataforma.' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.preco_funcionario_parceiro IS DISTINCT FROM OLD.preco_funcionario_parceiro
                        OR NEW.minimo_mensal_parceiro IS DISTINCT FROM OLD.minimo_mensal_parceiro
                        OR NEW.isencao_min_clientes_publicos IS DISTINCT FROM OLD.isencao_min_clientes_publicos)
                        AND NOT (personaliponto.acesso_sistema()
                            OR (OLD.canal_pai_id IS NOT NULL AND OLD.canal_pai_id = ANY(personaliponto.canais_canal()))) THEN
                        RAISE EXCEPTION 'Somente o canal ascendente define o preço do parceiro.' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END IF;
                IF NOT personaliponto.acesso_sistema() THEN
                    NEW.premium_isento_ate := NULL;
                    NEW.app_proprio_ativo := false;
                    NEW.app_proprio_desde := NULL;
                    NEW.app_proprio_implantacao_cobrada := false;
                END IF;
                IF NEW.canal_pai_id IS NULL THEN
                    NEW.nivel := 1;
                    NEW.caminho := '/' || NEW.id::text || '/';
                ELSE
                    SELECT c.nivel, c.caminho INTO pai FROM personaliponto.canais c WHERE c.id = NEW.canal_pai_id;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Canal pai inexistente ou fora da hierarquia.' USING ERRCODE = 'foreign_key_violation';
                    END IF;
                    NEW.nivel := pai.nivel + 1;
                    NEW.caminho := pai.caminho || NEW.id::text || '/';
                END IF;
                IF NEW.nivel > 3 THEN
                    RAISE EXCEPTION 'Profundidade máxima de canais excedida (Owner > Revendedor > Parceiro).' USING ERRCODE = 'check_violation';
                END IF;
                RETURN NEW;
            END;
            $f$;

        -- Fatura de canal: fora do modo sistema só o recebedor dá baixa/estorna, e apenas os campos de baixa mudam.
        CREATE OR REPLACE FUNCTION personaliponto.faturas_canal_protecao() RETURNS trigger
            LANGUAGE plpgsql AS
            $f$
            BEGIN
                IF personaliponto.acesso_sistema() THEN
                    RETURN NEW;
                END IF;
                IF OLD.canal_recebedor_id IS DISTINCT FROM personaliponto.canal_atual() THEN
                    RAISE EXCEPTION 'Somente o recebedor registra a baixa da fatura.' USING ERRCODE = 'insufficient_privilege';
                END IF;
                IF NEW.id <> OLD.id OR NEW.tipo <> OLD.tipo OR NEW.canal_pagador_id <> OLD.canal_pagador_id
                    OR NEW.canal_recebedor_id <> OLD.canal_recebedor_id OR NEW.competencia <> OLD.competencia
                    OR NEW.valor <> OLD.valor OR NEW.vencimento <> OLD.vencimento OR NEW.apuracao_id IS DISTINCT FROM OLD.apuracao_id
                    OR NEW.criada_em <> OLD.criada_em THEN
                    RAISE EXCEPTION 'Somente campos de baixa da fatura podem ser alterados.' USING ERRCODE = 'insufficient_privilege';
                END IF;
                RETURN NEW;
            END;
            $f$;

        CREATE OR REPLACE FUNCTION personaliponto.aplicar_rls() RETURNS void
            LANGUAGE plpgsql AS
            $f$
            DECLARE
                t record;
                p record;
                sis text := 'personaliponto.acesso_sistema()';
                canais text := 'ANY(personaliponto.canais_canal())';
                sel text; ins text; upd text; updchk text; del text;
            BEGIN
                FOR t IN
                    SELECT c.relname AS tabela,
                           EXISTS (SELECT 1 FROM information_schema.columns col
                                   WHERE col.table_schema = 'personaliponto' AND col.table_name = c.relname AND col.column_name = 'tenant_id') AS tem_tenant,
                           EXISTS (SELECT 1 FROM information_schema.columns col
                                   WHERE col.table_schema = 'personaliponto' AND col.table_name = c.relname AND col.column_name = 'canal_id') AS tem_canal
                    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'personaliponto' AND c.relkind = 'r' AND c.relname <> '__EFMigrationsHistory'
                LOOP
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON personaliponto.%I TO personaliponto_app', t.tabela);
                    EXECUTE format('ALTER TABLE personaliponto.%I ENABLE ROW LEVEL SECURITY', t.tabela);
                    EXECUTE format('ALTER TABLE personaliponto.%I FORCE ROW LEVEL SECURITY', t.tabela);
                    FOR p IN SELECT policyname FROM pg_policies WHERE schemaname = 'personaliponto' AND tablename = t.tabela LOOP
                        EXECUTE format('DROP POLICY IF EXISTS %I ON personaliponto.%I', p.policyname, t.tabela);
                    END LOOP;

                    sel := NULL; ins := NULL; upd := NULL; updchk := NULL; del := sis;
                    IF t.tabela = 'tenants' THEN
                        sel := sis || ' OR id = personaliponto.tenant_atual() OR id = ANY(personaliponto.tenants_canal())';
                        ins := sis || ' OR id = personaliponto.tenant_atual() OR canal_dono_id = ' || canais;
                        upd := sel; updchk := ins;
                    ELSIF t.tabela = 'canais' THEN
                        sel := sis || ' OR id = ' || canais;
                        ins := sis || ' OR canal_pai_id = ' || canais;
                        upd := sel; updchk := sis || ' OR id = ' || canais || ' OR canal_pai_id = ' || canais;
                    ELSIF t.tabela = ANY(ARRAY[{{string.Join(", ", TabelasCarteiraCanal.Select(x => "'" + x + "'"))}}]) THEN
                        sel := sis || ' OR tenant_id = personaliponto.tenant_atual() OR tenant_id = ANY(personaliponto.tenants_canal())';
                        ins := sel;
                        upd := sis || ' OR tenant_id = ANY(personaliponto.tenants_canal())'; updchk := upd;
                    ELSIF t.tabela = 'audit_logs' THEN
                        sel := sis || ' OR tenant_id = personaliponto.tenant_atual() OR canal_id = ' || canais;
                        ins := sel; upd := sis; updchk := sis;
                    ELSIF t.tabela = 'acessos_suporte' THEN
                        sel := sis || ' OR canal_id = ' || canais;
                        ins := sel; upd := sis; updchk := sis;
                    ELSIF t.tabela = 'faturas_canal' THEN
                        sel := sis || ' OR canal_pagador_id = ' || canais || ' OR canal_recebedor_id = personaliponto.canal_atual()';
                        ins := sis;
                        upd := sis || ' OR canal_recebedor_id = personaliponto.canal_atual()'; updchk := upd;
                    ELSIF t.tabela = 'itens_fatura_canal' THEN
                        sel := sis || ' OR EXISTS (SELECT 1 FROM personaliponto.faturas_canal f WHERE f.id = fatura_canal_id)';
                        ins := sis; upd := sis; updchk := sis;
                    ELSIF t.tabela = ANY(ARRAY['apuracoes_uso_canal', 'apuracoes_uso_tenant']) THEN
                        sel := sis || ' OR canal_id = ' || canais;
                        ins := sis; upd := sis; updchk := sis;
                    ELSIF t.tabela = 'assinaturas_premium' THEN
                        sel := sis || ' OR canal_id = ' || canais;
                        ins := sis || ' OR canal_id = personaliponto.canal_atual()';
                        upd := ins; updchk := ins;
                    ELSIF t.tem_tenant AND t.tem_canal THEN
                        sel := sis || ' OR tenant_id = personaliponto.tenant_atual() OR canal_id = ' || canais;
                        ins := sel; upd := sel; updchk := sel; del := sel;
                    ELSIF t.tem_tenant THEN
                        sel := sis || ' OR tenant_id = personaliponto.tenant_atual()';
                        ins := sel; upd := sel; updchk := sel; del := sel;
                    ELSIF t.tem_canal THEN
                        sel := sis || ' OR canal_id = ' || canais;
                        ins := sel; upd := sel; updchk := sel; del := sel;
                    END IF;

                    IF sel IS NULL THEN
                        -- Tabelas globais (planos, municípios, tabela de preço de canal): leitura livre, escrita só sistema.
                        EXECUTE format('CREATE POLICY leitura_global ON personaliponto.%I FOR SELECT USING (true)', t.tabela);
                        EXECUTE format('CREATE POLICY escrita_sistema ON personaliponto.%I FOR ALL USING (%s) WITH CHECK (%s)', t.tabela, sis, sis);
                    ELSE
                        EXECUTE format('CREATE POLICY p_select ON personaliponto.%I FOR SELECT USING (%s)', t.tabela, sel);
                        EXECUTE format('CREATE POLICY p_insert ON personaliponto.%I FOR INSERT WITH CHECK (%s)', t.tabela, ins);
                        EXECUTE format('CREATE POLICY p_update ON personaliponto.%I FOR UPDATE USING (%s) WITH CHECK (%s)', t.tabela, upd, updchk);
                        EXECUTE format('CREATE POLICY p_delete ON personaliponto.%I FOR DELETE USING (%s)', t.tabela, del);
                    END IF;
                END LOOP;
                EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA personaliponto TO personaliponto_app';
            END;
            $f$;

        DROP TRIGGER IF EXISTS tg_faturas_canal_protecao ON personaliponto.faturas_canal;
        CREATE TRIGGER tg_faturas_canal_protecao BEFORE UPDATE ON personaliponto.faturas_canal
            FOR EACH ROW EXECUTE FUNCTION personaliponto.faturas_canal_protecao();
        """;

    /// <summary>
    /// RLS v4 (E3/E9 — pagamentos): igual à v3 mais <c>contas_gateway</c> (só o próprio canal lê/grava; DELETE só
    /// sistema), <c>eventos_gateway</c> (só sistema) e proteção dos campos de cobrança do gateway em faturas_canal
    /// (o recebedor dá baixa manual, mas não altera link/Pix/boleto emitidos pelo sistema).
    /// </summary>
    public static string FuncoesPagamentos => FuncoesCanalV3
        .Replace("ELSIF t.tem_tenant AND t.tem_canal THEN",
            "ELSIF t.tabela = 'contas_gateway' THEN "
            + "sel := sis || ' OR canal_id = personaliponto.canal_atual()'; ins := sel; upd := sel; updchk := sel; "
            + "ELSIF t.tabela = 'eventos_gateway' THEN sel := sis; ins := sis; upd := sis; updchk := sis; "
            + "ELSIF t.tem_tenant AND t.tem_canal THEN")
        .Replace("OR NEW.criada_em <> OLD.criada_em THEN", """
OR NEW.criada_em <> OLD.criada_em
                    OR NEW.gateway_cobranca_id IS DISTINCT FROM OLD.gateway_cobranca_id OR NEW.gateway_conta_id IS DISTINCT FROM OLD.gateway_conta_id
                    OR NEW.gateway_link IS DISTINCT FROM OLD.gateway_link OR NEW.gateway_boleto_url IS DISTINCT FROM OLD.gateway_boleto_url
                    OR NEW.gateway_linha_digitavel IS DISTINCT FROM OLD.gateway_linha_digitavel
                    OR NEW.gateway_pix_copia_cola IS DISTINCT FROM OLD.gateway_pix_copia_cola OR NEW.gateway_pix_qr_code IS DISTINCT FROM OLD.gateway_pix_qr_code THEN
""");

    /// <summary>
    /// RLS v5 (E4 — white-label): igual à v4 mais <c>marcas_canal</c> (leitura pelo canal e ascendentes; INSERT/UPDATE
    /// só pelo próprio canal dono da marca — nem o revendedor edita a marca do parceiro; DELETE só sistema) e
    /// <c>dominios_canal</c> (leitura pela hierarquia; escrita só pela plataforma). A leitura pública da marca (tela de
    /// login, app) é feita pelo MarcaService em modo sistema controlado, devolvendo apenas dados de marca.
    /// Trigger: a versão publicada da marca nunca diminui.
    /// </summary>
    public static string FuncoesMarca => FuncoesPagamentos
        .Replace("ELSIF t.tem_tenant AND t.tem_canal THEN",
            "ELSIF t.tabela = 'marcas_canal' THEN "
            + "sel := sis || ' OR canal_id = ' || canais; ins := sis || ' OR canal_id = personaliponto.canal_atual()'; upd := ins; updchk := ins; "
            + "ELSIF t.tabela = 'dominios_canal' THEN sel := sis || ' OR canal_id = ' || canais; ins := sis; upd := sis; updchk := sis; "
            + "ELSIF t.tem_tenant AND t.tem_canal THEN")
        + """

        CREATE OR REPLACE FUNCTION personaliponto.marcas_canal_versao() RETURNS trigger
            LANGUAGE plpgsql AS
            $f$
            BEGIN
                IF NEW.canal_id <> OLD.canal_id THEN
                    RAISE EXCEPTION 'A marca não pode ser transferida para outro canal.' USING ERRCODE = 'insufficient_privilege';
                END IF;
                IF NEW.versao < OLD.versao THEN
                    RAISE EXCEPTION 'A versão publicada da marca não pode diminuir.' USING ERRCODE = 'check_violation';
                END IF;
                RETURN NEW;
            END;
            $f$;

        DROP TRIGGER IF EXISTS tg_marcas_canal_versao ON personaliponto.marcas_canal;
        CREATE TRIGGER tg_marcas_canal_versao BEFORE UPDATE ON personaliponto.marcas_canal
            FOR EACH ROW EXECUTE FUNCTION personaliponto.marcas_canal_versao();
        """;

    /// <summary>Tabela de preço padrão (decisões do dono §4), idempotente.</summary>
    public static string SemearTabelaPreco => $"""
        INSERT INTO personaliponto.tabelas_preco_canal (id, vigencia_inicio, preco_empresa_ativa, faixa1_ate, faixa1_preco, faixa2_ate, faixa2_preco,
            faixa3_preco, minimo_mensal, preco_premium, app_proprio_implantacao, app_proprio_mensal, observacao, criada_em)
        VALUES ('{Modules.SaaS.Domain.TabelaPrecoCanal.PadraoId}', DATE '2026-01-01', 12, 2000, 0.30, 5000, 0.25, 0.20, 290, 149, 1500, 200,
            'Tabela inicial (decisões do dono)', now())
        ON CONFLICT (id) DO NOTHING;
        """;

    /// <summary>Canal Owner raiz (idempotente). O trigger calcula nível e caminho.</summary>
    public static string SemearOwner => $"""
        INSERT INTO personaliponto.canais (id, tipo, canal_pai_id, caminho, nivel, slug, razao_social, cnpj, nome_marca, status, criado_em)
        VALUES ('{Modules.SaaS.Domain.Canal.OwnerRaizId}', 1, NULL, '', 1, 'personaliponto', 'PersonaliPonto', '', 'PersonaliPonto', 0, now())
        ON CONFLICT (id) DO NOTHING;
        """;
}
