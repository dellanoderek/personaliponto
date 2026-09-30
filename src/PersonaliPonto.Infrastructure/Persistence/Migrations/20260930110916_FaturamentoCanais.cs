using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaliPonto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FaturamentoCanais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "app_proprio_ativo",
                schema: "personaliponto",
                table: "canais",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "app_proprio_desde",
                schema: "personaliponto",
                table: "canais",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "app_proprio_implantacao_cobrada",
                schema: "personaliponto",
                table: "canais",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "isencao_min_clientes_publicos",
                schema: "personaliponto",
                table: "canais",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minimo_mensal_parceiro",
                schema: "personaliponto",
                table: "canais",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "preco_funcionario_parceiro",
                schema: "personaliponto",
                table: "canais",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "premium_isento_ate",
                schema: "personaliponto",
                table: "canais",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "apuracoes_uso_canal",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    empresas_ativas = table.Column<int>(type: "integer", nullable: false),
                    funcionarios_ativos = table.Column<int>(type: "integer", nullable: false),
                    gerada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apuracoes_uso_canal", x => x.id);
                    table.ForeignKey(
                        name: "fk_apuracoes_uso_canal_canais_canal_id",
                        column: x => x.canal_id,
                        principalSchema: "personaliponto",
                        principalTable: "canais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assinaturas_premium",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    cancelada_em = table.Column<DateOnly>(type: "date", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinaturas_premium", x => x.id);
                    table.ForeignKey(
                        name: "fk_assinaturas_premium_canais_canal_id",
                        column: x => x.canal_id,
                        principalSchema: "personaliponto",
                        principalTable: "canais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "faturas_canal",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    canal_pagador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_recebedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    apuracao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paga_em = table.Column<DateOnly>(type: "date", nullable: true),
                    valor_pago = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    forma_pagamento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faturas_canal", x => x.id);
                    table.ForeignKey(
                        name: "fk_faturas_canal_canais_canal_pagador_id",
                        column: x => x.canal_pagador_id,
                        principalSchema: "personaliponto",
                        principalTable: "canais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_faturas_canal_canais_canal_recebedor_id",
                        column: x => x.canal_recebedor_id,
                        principalSchema: "personaliponto",
                        principalTable: "canais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tabelas_preco_canal",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vigencia_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    preco_empresa_ativa = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    faixa1_ate = table.Column<int>(type: "integer", nullable: false),
                    faixa1_preco = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    faixa2_ate = table.Column<int>(type: "integer", nullable: false),
                    faixa2_preco = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    faixa3_preco = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    minimo_mensal = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    preco_premium = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    app_proprio_implantacao = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    app_proprio_mensal = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tabelas_preco_canal", x => x.id);
                    table.CheckConstraint("ck_tabelas_preco_canal_faixas", "faixa1_ate > 0 AND faixa2_ate > faixa1_ate");
                });

            migrationBuilder.CreateTable(
                name: "apuracoes_uso_tenant",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    apuracao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_dono_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_cliente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status_cliente = table.Column<int>(type: "integer", nullable: false),
                    tipo_entidade = table.Column<int>(type: "integer", nullable: false),
                    empresa_ativa = table.Column<bool>(type: "boolean", nullable: false),
                    funcionarios_ativos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apuracoes_uso_tenant", x => x.id);
                    table.ForeignKey(
                        name: "fk_apuracoes_uso_tenant_apuracoes_uso_canal_apuracao_id",
                        column: x => x.apuracao_id,
                        principalSchema: "personaliponto",
                        principalTable: "apuracoes_uso_canal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "itens_fatura_canal",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fatura_canal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantidade = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    valor_unitario = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_fatura_canal", x => x.id);
                    table.ForeignKey(
                        name: "fk_itens_fatura_canal_faturas_canal_fatura_canal_id",
                        column: x => x.fatura_canal_id,
                        principalSchema: "personaliponto",
                        principalTable: "faturas_canal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_apuracoes_uso_canal_canal_id_competencia",
                schema: "personaliponto",
                table: "apuracoes_uso_canal",
                columns: new[] { "canal_id", "competencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apuracoes_uso_tenant_apuracao_id_cliente_id",
                schema: "personaliponto",
                table: "apuracoes_uso_tenant",
                columns: new[] { "apuracao_id", "cliente_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apuracoes_uso_tenant_canal_id",
                schema: "personaliponto",
                table: "apuracoes_uso_tenant",
                column: "canal_id");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_premium_vigente",
                schema: "personaliponto",
                table: "assinaturas_premium",
                column: "canal_id",
                unique: true,
                filter: "cancelada_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_faturas_canal_canal_pagador_id_canal_recebedor_id_competenc~",
                schema: "personaliponto",
                table: "faturas_canal",
                columns: new[] { "canal_pagador_id", "canal_recebedor_id", "competencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_faturas_canal_canal_recebedor_id_competencia",
                schema: "personaliponto",
                table: "faturas_canal",
                columns: new[] { "canal_recebedor_id", "competencia" });

            migrationBuilder.CreateIndex(
                name: "ix_itens_fatura_canal_fatura_canal_id",
                schema: "personaliponto",
                table: "itens_fatura_canal",
                column: "fatura_canal_id");

            migrationBuilder.CreateIndex(
                name: "ix_tabelas_preco_canal_vigencia_inicio",
                schema: "personaliponto",
                table: "tabelas_preco_canal",
                column: "vigencia_inicio",
                unique: true);

            // E2: triggers (condições comerciais do canal, baixa de fatura de canal), snapshots imutáveis,
            // tabela de preço padrão e RLS v3 por comando. Sempre por último: aplicar_rls em toda tabela.
            migrationBuilder.Sql(SegurancaBanco.FuncoesCanalV3);
            migrationBuilder.Sql(SegurancaBanco.ImutabilidadeCanal());
            migrationBuilder.Sql(SegurancaBanco.SemearTabelaPreco);
            migrationBuilder.Sql(SegurancaBanco.Aplicar);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volta à RLS v2 (E1) antes de remover tabelas/colunas referenciadas pelas políticas v3.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tg_faturas_canal_protecao ON personaliponto.faturas_canal;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS personaliponto.faturas_canal_protecao();");
            migrationBuilder.Sql(SegurancaBanco.RemoverPoliticas);
            migrationBuilder.Sql(SegurancaBanco.FuncoesCanal);
            migrationBuilder.Sql(SegurancaBanco.Aplicar);

            migrationBuilder.DropTable(
                name: "apuracoes_uso_tenant",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "assinaturas_premium",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "itens_fatura_canal",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "tabelas_preco_canal",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "apuracoes_uso_canal",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "faturas_canal",
                schema: "personaliponto");

            migrationBuilder.DropColumn(
                name: "app_proprio_ativo",
                schema: "personaliponto",
                table: "canais");

            migrationBuilder.DropColumn(
                name: "app_proprio_desde",
                schema: "personaliponto",
                table: "canais");

            migrationBuilder.DropColumn(
                name: "app_proprio_implantacao_cobrada",
                schema: "personaliponto",
                table: "canais");

            migrationBuilder.DropColumn(
                name: "isencao_min_clientes_publicos",
                schema: "personaliponto",
                table: "canais");

            migrationBuilder.DropColumn(
                name: "minimo_mensal_parceiro",
                schema: "personaliponto",
                table: "canais");

            migrationBuilder.DropColumn(
                name: "preco_funcionario_parceiro",
                schema: "personaliponto",
                table: "canais");

            migrationBuilder.DropColumn(
                name: "premium_isento_ate",
                schema: "personaliponto",
                table: "canais");
        }
    }
}
