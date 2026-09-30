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
}
