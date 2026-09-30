using System;
using PersonaliPonto.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaliPonto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "personaliponto");

            migrationBuilder.CreateTable(
                name: "acessos_suporte",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_nome = table.Column<string>(type: "text", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: false),
                    inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acessos_suporte", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ancoras_hora",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hora_servidor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ancoras_hora", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "arquivos",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket = table.Column<string>(type: "text", nullable: false),
                    caminho = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    tamanho = table.Column<long>(type: "bigint", nullable: false),
                    nome_original = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquivos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assinaturas_espelho",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    concorda = table.Column<bool>(type: "boolean", nullable: false),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    hash_espelho = table.Column<string>(type: "text", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: true),
                    assinado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinaturas_espelho", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "atestados",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    data_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    data_fim = table.Column<DateOnly>(type: "date", nullable: false),
                    minutos_parciais = table.Column<int>(type: "integer", nullable: true),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    parecer_rh = table.Column<string>(type: "text", nullable: true),
                    analisado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    analisado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_atestados", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_nome = table.Column<string>(type: "text", nullable: true),
                    acao = table.Column<string>(type: "text", nullable: false),
                    entidade = table.Column<string>(type: "text", nullable: false),
                    entidade_id = table.Column<string>(type: "text", nullable: true),
                    dados = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true),
                    em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "banco_horas",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    minutos = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    fechamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estorna_lancamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_por_nome = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banco_horas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "configuracoes_seguranca",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exigir_dispositivo_autorizado = table.Column<bool>(type: "boolean", nullable: false),
                    foto_na_marcacao = table.Column<bool>(type: "boolean", nullable: false),
                    retencao_foto_dias = table.Column<int>(type: "integer", nullable: false),
                    avaliacao_lgpd_responsavel = table.Column<string>(type: "text", nullable: true),
                    avaliacao_lgpd_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracoes_seguranca", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dispositivos",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    primeiro_uso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    alterado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispositivos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "empregadores",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    razao_social = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    nome_fantasia = table.Column<string>(type: "text", nullable: true),
                    tipo_identificador = table.Column<int>(type: "integer", nullable: false),
                    identificador = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empregadores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "escalas",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jornada_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fim = table.Column<DateOnly>(type: "date", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escalas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faturas",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paga_em = table.Column<DateOnly>(type: "date", nullable: true),
                    valor_pago = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    forma_pagamento = table.Column<string>(type: "text", nullable: true),
                    observacao = table.Column<string>(type: "text", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faturas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fechamentos",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    minutos_trabalhados = table.Column<int>(type: "integer", nullable: false),
                    minutos_extras = table.Column<int>(type: "integer", nullable: false),
                    minutos_atrasos = table.Column<int>(type: "integer", nullable: false),
                    minutos_faltas = table.Column<int>(type: "integer", nullable: false),
                    dias_inconsistentes = table.Column<int>(type: "integer", nullable: false),
                    fechado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fechado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reaberto_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fechamentos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feriados",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    estabelecimento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    abrangencia = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feriados", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fotos_marcacao",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registro_rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    excluida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fotos_marcacao", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jornadas",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    ciclo_dias = table.Column<int>(type: "integer", nullable: true),
                    data_referencia_ciclo = table.Column<DateOnly>(type: "date", nullable: true),
                    tolerancia_minutos = table.Column<int>(type: "integer", nullable: false),
                    tolerancia_diaria_minutos = table.Column<int>(type: "integer", nullable: false),
                    horarios = table.Column<string>(type: "jsonb", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jornadas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nsr_contadores",
                schema: "personaliponto",
                columns: table => new
                {
                    estabelecimento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ultimo_nsr = table.Column<long>(type: "bigint", nullable: false),
                    ultimo_hash_tipo7 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nsr_contadores", x => x.estabelecimento_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    erro = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planos",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    preco_mensal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    limite_funcionarios = table.Column<int>(type: "integer", nullable: false),
                    preco_funcionario_excedente = table.Column<decimal>(type: "numeric", nullable: false),
                    permite_app = table.Column<bool>(type: "boolean", nullable: false),
                    permite_terminal_web = table.Column<bool>(type: "boolean", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "politicas_banco_horas",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    extras_para_banco = table.Column<bool>(type: "boolean", nullable: false),
                    descontar_atrasos = table.Column<bool>(type: "boolean", nullable: false),
                    validade_meses = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_politicas_banco_horas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revogado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    substituido_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "solicitacoes",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    horario = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    registro_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resposta_rh = table.Column<string>(type: "text", nullable: true),
                    respondido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    respondido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tratamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitacoes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    teste_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_contato = table.Column<string>(type: "text", nullable: true),
                    telefone_contato = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminais_web",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    estabelecimento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminais_web", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tratamentos",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    registro_rep_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tratamento_revogado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offset_minutos = table.Column<int>(type: "integer", nullable: true),
                    motivo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_nome = table.Column<string>(type: "text", nullable: true),
                    solicitacao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tratamentos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    senha_hash = table.Column<string>(type: "text", nullable: false),
                    papel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    deve_trocar_senha = table.Column<bool>(type: "boolean", nullable: false),
                    tentativas_falhas = table.Column<int>(type: "integer", nullable: false),
                    bloqueado_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mfa_habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_segredo = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_login = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "estabelecimentos",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empregador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo_identificador = table.Column<int>(type: "integer", nullable: false),
                    identificador = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    cno_caepf = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    local_prestacao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fuso_horario = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    raio_cerca_metros = table.Column<int>(type: "integer", nullable: true),
                    modo_cerca = table.Column<int>(type: "integer", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estabelecimentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_estabelecimentos_empregadores_empregador_id",
                        column: x => x.empregador_id,
                        principalSchema: "personaliponto",
                        principalTable: "empregadores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assinaturas",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: true),
                    razao_social = table.Column<string>(type: "text", nullable: false),
                    cnpj = table.Column<string>(type: "text", nullable: false),
                    telefone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    valor_mensal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    funcionarios_contratados = table.Column<int>(type: "integer", nullable: false),
                    custo_mensal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    fornecedor = table.Column<string>(type: "text", nullable: true),
                    dia_vencimento = table.Column<int>(type: "integer", nullable: false),
                    dias_tolerancia = table.Column<int>(type: "integer", nullable: false),
                    dias_para_bloqueio = table.Column<int>(type: "integer", nullable: false),
                    bloqueio_automatico = table.Column<bool>(type: "boolean", nullable: false),
                    inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    cancelada_em = table.Column<DateOnly>(type: "date", nullable: true),
                    motivo_cancelamento = table.Column<string>(type: "text", nullable: true),
                    observacoes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinaturas", x => x.id);
                    table.ForeignKey(
                        name: "fk_assinaturas_planos_plano_id",
                        column: x => x.plano_id,
                        principalSchema: "personaliponto",
                        principalTable: "planos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "funcionarios",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    estabelecimento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    matricula = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    matricula_esocial = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    data_admissao = table.Column<DateOnly>(type: "date", nullable: false),
                    data_demissao = table.Column<DateOnly>(type: "date", nullable: true),
                    cargo = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    jornada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gestor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pin_hash = table.Column<string>(type: "text", nullable: true),
                    pin_tentativas_falhas = table.Column<int>(type: "integer", nullable: false),
                    pin_bloqueado_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    permite_marcacao_app = table.Column<bool>(type: "boolean", nullable: false),
                    permite_marcacao_offline = table.Column<bool>(type: "boolean", nullable: false),
                    modo_cerca_funcionario = table.Column<int>(type: "integer", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funcionarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_funcionarios_estabelecimentos_estabelecimento_id",
                        column: x => x.estabelecimento_id,
                        principalSchema: "personaliponto",
                        principalTable: "estabelecimentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_funcionarios_jornadas_jornada_id",
                        column: x => x.jornada_id,
                        principalSchema: "personaliponto",
                        principalTable: "jornadas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registros_rep",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estabelecimento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nsr = table.Column<long>(type: "bigint", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    data_hora_gravacao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    offset_gravacao_minutos = table.Column<int>(type: "integer", nullable: false),
                    funcionario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    data_hora_marcacao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offset_marcacao_minutos = table.Column<int>(type: "integer", nullable: true),
                    coletor = table.Column<int>(type: "integer", nullable: true),
                    offline = table.Column<bool>(type: "boolean", nullable: true),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_identificador_empregador = table.Column<int>(type: "integer", nullable: true),
                    identificador_empregador = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    cno_caepf = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    razao_social = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    local_prestacao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cpf_responsavel = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    data_hora_antes_ajuste = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    data_hora_ajustada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tipo_operacao = table.Column<char>(type: "character(1)", nullable: true),
                    nome_empregado = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    demais_dados = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    tipo_evento = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registros_rep", x => x.id);
                    table.ForeignKey(
                        name: "fk_registros_rep_estabelecimentos_estabelecimento_id",
                        column: x => x.estabelecimento_id,
                        principalSchema: "personaliponto",
                        principalTable: "estabelecimentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_registros_rep_funcionarios_funcionario_id",
                        column: x => x.funcionario_id,
                        principalSchema: "personaliponto",
                        principalTable: "funcionarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "marcacoes_contexto",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registro_rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    dentro_cerca = table.Column<bool>(type: "boolean", nullable: true),
                    dispositivo_id = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    horario_informado_coletor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marcacoes_contexto", x => x.id);
                    table.ForeignKey(
                        name: "fk_marcacoes_contexto_registros_rep_registro_rep_id",
                        column: x => x.registro_rep_id,
                        principalSchema: "personaliponto",
                        principalTable: "registros_rep",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acessos_suporte_tenant_id_inicio",
                schema: "personaliponto",
                table: "acessos_suporte",
                columns: new[] { "tenant_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_tenant_id_criado_em",
                schema: "personaliponto",
                table: "arquivos",
                columns: new[] { "tenant_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_cnpj",
                schema: "personaliponto",
                table: "assinaturas",
                column: "cnpj");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_plano_id",
                schema: "personaliponto",
                table: "assinaturas",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_tenant_id",
                schema: "personaliponto",
                table: "assinaturas",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_espelho_funcionario_id_ano_mes",
                schema: "personaliponto",
                table: "assinaturas_espelho",
                columns: new[] { "funcionario_id", "ano", "mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_atestados_tenant_id_funcionario_id_data_inicio",
                schema: "personaliponto",
                table: "atestados",
                columns: new[] { "tenant_id", "funcionario_id", "data_inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_em",
                schema: "personaliponto",
                table: "audit_logs",
                columns: new[] { "tenant_id", "em" });

            migrationBuilder.CreateIndex(
                name: "ix_banco_horas_tenant_id_funcionario_id_data",
                schema: "personaliponto",
                table: "banco_horas",
                columns: new[] { "tenant_id", "funcionario_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_configuracoes_seguranca_tenant_id",
                schema: "personaliponto",
                table: "configuracoes_seguranca",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispositivos_funcionario_id_dispositivo_id",
                schema: "personaliponto",
                table: "dispositivos",
                columns: new[] { "funcionario_id", "dispositivo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_escalas_funcionario_id_inicio",
                schema: "personaliponto",
                table: "escalas",
                columns: new[] { "funcionario_id", "inicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_estabelecimentos_empregador_id",
                schema: "personaliponto",
                table: "estabelecimentos",
                column: "empregador_id");

            migrationBuilder.CreateIndex(
                name: "ix_estabelecimentos_tenant_id_identificador",
                schema: "personaliponto",
                table: "estabelecimentos",
                columns: new[] { "tenant_id", "identificador" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_faturas_tenant_id_competencia",
                schema: "personaliponto",
                table: "faturas",
                columns: new[] { "tenant_id", "competencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fechamentos_funcionario_id_ano_mes",
                schema: "personaliponto",
                table: "fechamentos",
                columns: new[] { "funcionario_id", "ano", "mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feriados_tenant_id_data",
                schema: "personaliponto",
                table: "feriados",
                columns: new[] { "tenant_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_fotos_marcacao_excluida_em_expira_em",
                schema: "personaliponto",
                table: "fotos_marcacao",
                columns: new[] { "excluida_em", "expira_em" });

            migrationBuilder.CreateIndex(
                name: "ix_fotos_marcacao_registro_rep_id",
                schema: "personaliponto",
                table: "fotos_marcacao",
                column: "registro_rep_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_funcionarios_estabelecimento_id",
                schema: "personaliponto",
                table: "funcionarios",
                column: "estabelecimento_id");

            migrationBuilder.CreateIndex(
                name: "ix_funcionarios_jornada_id",
                schema: "personaliponto",
                table: "funcionarios",
                column: "jornada_id");

            migrationBuilder.CreateIndex(
                name: "ix_funcionarios_tenant_id_cpf",
                schema: "personaliponto",
                table: "funcionarios",
                columns: new[] { "tenant_id", "cpf" });

            migrationBuilder.CreateIndex(
                name: "ix_funcionarios_tenant_id_estabelecimento_id_matricula",
                schema: "personaliponto",
                table: "funcionarios",
                columns: new[] { "tenant_id", "estabelecimento_id", "matricula" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jornadas_tenant_id_codigo",
                schema: "personaliponto",
                table: "jornadas",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_marcacoes_contexto_registro_rep_id",
                schema: "personaliponto",
                table: "marcacoes_contexto",
                column: "registro_rep_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_processado_em_proxima_tentativa",
                schema: "personaliponto",
                table: "outbox",
                columns: new[] { "processado_em", "proxima_tentativa" });

            migrationBuilder.CreateIndex(
                name: "ix_politicas_banco_horas_tenant_id",
                schema: "personaliponto",
                table: "politicas_banco_horas",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "personaliponto",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registros_rep_estabelecimento_id_data_hora_gravacao",
                schema: "personaliponto",
                table: "registros_rep",
                columns: new[] { "estabelecimento_id", "data_hora_gravacao" });

            migrationBuilder.CreateIndex(
                name: "ix_registros_rep_estabelecimento_id_nsr",
                schema: "personaliponto",
                table: "registros_rep",
                columns: new[] { "estabelecimento_id", "nsr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registros_rep_funcionario_id",
                schema: "personaliponto",
                table: "registros_rep",
                column: "funcionario_id");

            migrationBuilder.CreateIndex(
                name: "ix_registros_rep_tenant_id_client_id",
                schema: "personaliponto",
                table: "registros_rep",
                columns: new[] { "tenant_id", "client_id" },
                unique: true,
                filter: "client_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registros_rep_tenant_id_funcionario_id_data_hora_marcacao",
                schema: "personaliponto",
                table: "registros_rep",
                columns: new[] { "tenant_id", "funcionario_id", "data_hora_marcacao" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_funcionario_id",
                schema: "personaliponto",
                table: "solicitacoes",
                column: "funcionario_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitacoes_tenant_id_status",
                schema: "personaliponto",
                table: "solicitacoes",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "personaliponto",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terminais_web_token_hash",
                schema: "personaliponto",
                table: "terminais_web",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tratamentos_registro_rep_id",
                schema: "personaliponto",
                table: "tratamentos",
                column: "registro_rep_id");

            migrationBuilder.CreateIndex(
                name: "ix_tratamentos_tenant_id_funcionario_id_data_hora",
                schema: "personaliponto",
                table: "tratamentos",
                columns: new[] { "tenant_id", "funcionario_id", "data_hora" });

            migrationBuilder.CreateIndex(
                name: "ix_tratamentos_tratamento_revogado_id",
                schema: "personaliponto",
                table: "tratamentos",
                column: "tratamento_revogado_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                schema: "personaliponto",
                table: "usuarios",
                column: "email",
                unique: true);

            // Segurança: papel da aplicação, RLS forçada em todas as tabelas e imutabilidade física do ARP.
            migrationBuilder.Sql(SegurancaBanco.Funcoes);
            migrationBuilder.Sql(SegurancaBanco.Imutabilidade());
            migrationBuilder.Sql(SegurancaBanco.Aplicar);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acessos_suporte",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "ancoras_hora",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "arquivos",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "assinaturas",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "assinaturas_espelho",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "atestados",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "banco_horas",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "configuracoes_seguranca",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "dispositivos",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "escalas",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "faturas",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "fechamentos",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "feriados",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "fotos_marcacao",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "marcacoes_contexto",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "nsr_contadores",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "politicas_banco_horas",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "solicitacoes",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "terminais_web",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "tratamentos",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "usuarios",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "planos",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "registros_rep",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "funcionarios",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "estabelecimentos",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "jornadas",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "empregadores",
                schema: "personaliponto");
        }
    }
}
