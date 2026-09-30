using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PersonaliPonto.Infrastructure.Persistence.MigrationsSistema
{
    /// <inheritdoc />
    public partial class SistemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "personaliponto_sistema");

            migrationBuilder.CreateTable(
                name: "configuracoes",
                schema: "personaliponto_sistema",
                columns: table => new
                {
                    chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valor = table.Column<string>(type: "text", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuracoes", x => x.chave);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "personaliponto_sistema",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                });
            // Schema interno: RLS sem políticas (só o dono acessa) e nenhum acesso pela Data API do Supabase.
            migrationBuilder.Sql("""
                ALTER TABLE personaliponto_sistema.data_protection_keys ENABLE ROW LEVEL SECURITY;
                ALTER TABLE personaliponto_sistema.configuracoes ENABLE ROW LEVEL SECURITY;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN EXECUTE 'REVOKE ALL ON SCHEMA personaliponto_sistema FROM anon'; END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN EXECUTE 'REVOKE ALL ON SCHEMA personaliponto_sistema FROM authenticated'; END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'personaliponto_app') THEN EXECUTE 'REVOKE ALL ON SCHEMA personaliponto_sistema FROM personaliponto_app'; END IF;
                END $$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracoes",
                schema: "personaliponto_sistema");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "personaliponto_sistema");
        }
    }
}
