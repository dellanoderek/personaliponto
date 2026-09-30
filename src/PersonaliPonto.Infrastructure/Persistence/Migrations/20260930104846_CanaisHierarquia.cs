using System;
using Microsoft.EntityFrameworkCore.Migrations;
using PersonaliPonto.Modules.SaaS.Domain;

#nullable disable

namespace PersonaliPonto.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Etapa E1: hierarquia de canais (Owner → Revendedor → Parceiro) com caminho materializado, município (IBGE),
    /// canal dono obrigatório no tenant (clientes existentes vão para o Owner raiz), usuários com escopo
    /// (Plataforma/Canal/Tenant), suporte generalizado por nível e RLS v2 de canal.
    /// </summary>
    public partial class CanaisHierarquia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "canal_id",
                schema: "personaliponto",
                table: "usuarios",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "escopo",
                schema: "personaliponto",
                table: "usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Escopo derivado dos dados existentes: sem tenant = plataforma; com tenant = cliente.
            migrationBuilder.Sql("UPDATE personaliponto.usuarios SET escopo = CASE WHEN tenant_id IS NULL THEN 0 ELSE 2 END;");

            // Clientes existentes passam a pertencer ao Owner raiz (default removido ao final).
            migrationBuilder.AddColumn<Guid>(
                name: "canal_dono_id",
                schema: "personaliponto",
                table: "tenants",
                type: "uuid",
                nullable: false,
                defaultValue: Canal.OwnerRaizId);

            migrationBuilder.AddColumn<Guid>(
                name: "municipio_id",
                schema: "personaliponto",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tipo_entidade",
                schema: "personaliponto",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "canal_id",
                schema: "personaliponto",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "personaliponto",
                table: "acessos_suporte",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "canal_alvo_id",
                schema: "personaliponto",
                table: "acessos_suporte",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "canal_id",
                schema: "personaliponto",
                table: "acessos_suporte",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "nivel_origem",
                schema: "personaliponto",
                table: "acessos_suporte",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "canais",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    canal_pai_id = table.Column<Guid>(type: "uuid", nullable: true),
                    caminho = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nivel = table.Column<int>(type: "integer", nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    razao_social = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    nome_marca = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_canais", x => x.id);
                    table.CheckConstraint("ck_canais_nivel", "nivel BETWEEN 1 AND 3 AND nivel = tipo");
                    table.CheckConstraint("ck_canais_pai", "(tipo = 1) = (canal_pai_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_canais_canais_canal_pai_id",
                        column: x => x.canal_pai_id,
                        principalSchema: "personaliponto",
                        principalTable: "canais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Funções/trigger de hierarquia e RLS v2; em seguida o Owner raiz (idempotente).
            migrationBuilder.Sql(SegurancaBanco.FuncoesCanal);
            migrationBuilder.Sql(SegurancaBanco.SemearOwner);

            migrationBuilder.CreateTable(
                name: "municipios",
                schema: "personaliponto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_ibge = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    uf = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_municipios", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_canal_id",
                schema: "personaliponto",
                table: "usuarios",
                column: "canal_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_escopo",
                schema: "personaliponto",
                table: "usuarios",
                sql: "(escopo = 0 AND tenant_id IS NULL AND canal_id IS NULL) OR (escopo = 1 AND canal_id IS NOT NULL AND tenant_id IS NULL) OR (escopo = 2 AND tenant_id IS NOT NULL AND canal_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_canal_dono_id",
                schema: "personaliponto",
                table: "tenants",
                column: "canal_dono_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_municipio_id",
                schema: "personaliponto",
                table: "tenants",
                column: "municipio_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_canal_id_em",
                schema: "personaliponto",
                table: "audit_logs",
                columns: new[] { "canal_id", "em" });

            migrationBuilder.CreateIndex(
                name: "ix_acessos_suporte_canal_id_inicio",
                schema: "personaliponto",
                table: "acessos_suporte",
                columns: new[] { "canal_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_canais_caminho",
                schema: "personaliponto",
                table: "canais",
                column: "caminho",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_canais_canal_pai_id",
                schema: "personaliponto",
                table: "canais",
                column: "canal_pai_id");

            migrationBuilder.CreateIndex(
                name: "ix_canais_owner_unico",
                schema: "personaliponto",
                table: "canais",
                column: "tipo",
                unique: true,
                filter: "tipo = 1");

            migrationBuilder.CreateIndex(
                name: "ix_canais_slug",
                schema: "personaliponto",
                table: "canais",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_municipios_codigo_ibge",
                schema: "personaliponto",
                table: "municipios",
                column: "codigo_ibge",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_municipios_uf_nome",
                schema: "personaliponto",
                table: "municipios",
                columns: new[] { "uf", "nome" });

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_canais_canal_dono_id",
                schema: "personaliponto",
                table: "tenants",
                column: "canal_dono_id",
                principalSchema: "personaliponto",
                principalTable: "canais",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_municipios_municipio_id",
                schema: "personaliponto",
                table: "tenants",
                column: "municipio_id",
                principalSchema: "personaliponto",
                principalTable: "municipios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_usuarios_canais_canal_id",
                schema: "personaliponto",
                table: "usuarios",
                column: "canal_id",
                principalSchema: "personaliponto",
                principalTable: "canais",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("ALTER TABLE personaliponto.tenants ALTER COLUMN canal_dono_id DROP DEFAULT;");

            // Sempre por último: RLS forçada e políticas em toda tabela (inclusive as novas).
            migrationBuilder.Sql(SegurancaBanco.Aplicar);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tg_tenants_canal_dono ON personaliponto.tenants;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS personaliponto.tenants_canal_dono();");
            // Volta à RLS v1 antes de remover colunas referenciadas pelas políticas v2: remove todas as políticas
            // (inclusive isolamento_canal/leitura_global/escrita_sistema criadas pela v2) e reaplica a v1.
            migrationBuilder.Sql(SegurancaBanco.RemoverPoliticas);
            migrationBuilder.Sql(SegurancaBanco.Funcoes);
            migrationBuilder.Sql(SegurancaBanco.Aplicar);

            migrationBuilder.DropForeignKey(
                name: "fk_tenants_canais_canal_dono_id",
                schema: "personaliponto",
                table: "tenants");

            migrationBuilder.DropForeignKey(
                name: "fk_tenants_municipios_municipio_id",
                schema: "personaliponto",
                table: "tenants");

            migrationBuilder.DropForeignKey(
                name: "fk_usuarios_canais_canal_id",
                schema: "personaliponto",
                table: "usuarios");

            migrationBuilder.DropTable(
                name: "canais",
                schema: "personaliponto");

            migrationBuilder.DropTable(
                name: "municipios",
                schema: "personaliponto");

            migrationBuilder.DropIndex(
                name: "ix_usuarios_canal_id",
                schema: "personaliponto",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_escopo",
                schema: "personaliponto",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "ix_tenants_canal_dono_id",
                schema: "personaliponto",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_tenants_municipio_id",
                schema: "personaliponto",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_canal_id_em",
                schema: "personaliponto",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_acessos_suporte_canal_id_inicio",
                schema: "personaliponto",
                table: "acessos_suporte");

            migrationBuilder.DropColumn(
                name: "canal_id",
                schema: "personaliponto",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "escopo",
                schema: "personaliponto",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "canal_dono_id",
                schema: "personaliponto",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "municipio_id",
                schema: "personaliponto",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "tipo_entidade",
                schema: "personaliponto",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "canal_id",
                schema: "personaliponto",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "canal_alvo_id",
                schema: "personaliponto",
                table: "acessos_suporte");

            migrationBuilder.DropColumn(
                name: "canal_id",
                schema: "personaliponto",
                table: "acessos_suporte");

            migrationBuilder.DropColumn(
                name: "nivel_origem",
                schema: "personaliponto",
                table: "acessos_suporte");

            // Acessos a painéis de canal (sem cliente) não existem na v1: removidos antes do NOT NULL.
            migrationBuilder.Sql("DELETE FROM personaliponto.acessos_suporte WHERE tenant_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "personaliponto",
                table: "acessos_suporte",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS personaliponto.canais_hierarquia();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS personaliponto.canal_atual();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS personaliponto.canais_canal();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS personaliponto.tenants_canal();");
        }
    }
}
