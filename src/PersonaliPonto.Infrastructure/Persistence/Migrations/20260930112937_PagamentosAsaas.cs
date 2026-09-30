using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonaliPonto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PagamentosAsaas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_faturas_canal_canal_pagador_id_canal_recebedor_id_competenc~",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.AddColumn<string>(
                name: "gateway_boleto_url",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "gateway_conta_id",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_erro",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_linha_digitavel",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_link",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_pix_copia_cola",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_pix_qr_code",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gateway_status",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "gateway_ultimo_evento_em",
                schema: "personaliponto",
                table: "faturas_canal",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_boleto_url",
                schema: "personaliponto",
                table: "faturas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "gateway_conta_id",
                schema: "personaliponto",
                table: "faturas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_erro",
                schema: "personaliponto",
                table: "faturas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_linha_digitavel",
                schema: "personaliponto",
                table: "faturas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_link",
                schema: "personaliponto",
                table: "faturas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_pix_copia_cola",
                schema: "personaliponto",
                table: "faturas",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_pix_qr_code",
                schema: "personaliponto",
                table: "faturas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gateway_status",
                schema: "personaliponto",
                table: "faturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "gateway_ultimo_evento_em",
                schema: "personaliponto",
                table: "faturas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contas_gateway",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provedor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    chave_cifrada = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    chave_final = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    webhook_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    webhook_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nome_conta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    documento_conta = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    conectada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    conectada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    desconectada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contas_gateway", x => x.id);
                    table.ForeignKey(
                        name: "fk_contas_gateway_canais_canal_id",
                        column: x => x.canal_id,
                        principalSchema: "personaliponto",
                        principalTable: "canais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_gateway",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    evento_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cobranca_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recebido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resultado = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_gateway", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_faturas_canal_canal_pagador_id",
                schema: "personaliponto",
                table: "faturas_canal",
                column: "canal_pagador_id");

            migrationBuilder.CreateIndex(
                name: "ix_faturas_canal_gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas_canal",
                column: "gateway_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_faturas_canal_mensal_unica",
                schema: "personaliponto",
                table: "faturas_canal",
                columns: new[] { "canal_pagador_id", "canal_recebedor_id", "competencia" },
                unique: true,
                filter: "tipo <> 3");

            migrationBuilder.CreateIndex(
                name: "ix_faturas_gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas",
                column: "gateway_cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_contas_gateway_canal_ativa",
                schema: "personaliponto",
                table: "contas_gateway",
                column: "canal_id",
                unique: true,
                filter: "ativa");

            migrationBuilder.CreateIndex(
                name: "ix_contas_gateway_webhook_token_hash",
                schema: "personaliponto",
                table: "contas_gateway",
                column: "webhook_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_eventos_gateway_cobranca_id",
                schema: "personaliponto",
                table: "eventos_gateway",
                column: "cobranca_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_gateway_origem_evento_id",
                schema: "personaliponto",
                table: "eventos_gateway",
                columns: new[] { "origem", "evento_id" },
                unique: true);

            // RLS v4: contas_gateway só do próprio canal, eventos_gateway só sistema, campos do gateway protegidos.
            migrationBuilder.Sql(SegurancaBanco.FuncoesPagamentos);
            migrationBuilder.Sql(SegurancaBanco.Aplicar);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contas_gateway",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "eventos_gateway",
                schema: "personaliponto");

            migrationBuilder.DropIndex(
                name: "ix_faturas_canal_canal_pagador_id",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropIndex(
                name: "ix_faturas_canal_gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropIndex(
                name: "ix_faturas_canal_mensal_unica",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropIndex(
                name: "ix_faturas_gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_boleto_url",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_conta_id",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_erro",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_linha_digitavel",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_link",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_pix_copia_cola",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_pix_qr_code",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_status",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_ultimo_evento_em",
                schema: "personaliponto",
                table: "faturas_canal");

            migrationBuilder.DropColumn(
                name: "gateway_boleto_url",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_cobranca_id",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_conta_id",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_erro",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_linha_digitavel",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_link",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_pix_copia_cola",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_pix_qr_code",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_status",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.DropColumn(
                name: "gateway_ultimo_evento_em",
                schema: "personaliponto",
                table: "faturas");

            migrationBuilder.CreateIndex(
                name: "ix_faturas_canal_canal_pagador_id_canal_recebedor_id_competenc~",
                schema: "personaliponto",
                table: "faturas_canal",
                columns: new[] { "canal_pagador_id", "canal_recebedor_id", "competencia" },
                unique: true);

            migrationBuilder.Sql(SegurancaBanco.RemoverPoliticas);
            migrationBuilder.Sql(SegurancaBanco.FuncoesCanalV3);
            migrationBuilder.Sql(SegurancaBanco.Aplicar);
        }
    }
}
