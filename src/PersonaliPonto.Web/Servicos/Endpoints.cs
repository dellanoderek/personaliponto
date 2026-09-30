using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Documentos;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.Analytics;
using PersonaliPonto.Modules.RH.Services;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Web.Servicos;

public static class ContaEndpoints
{
    public static void MapContaEndpoints(this WebApplication app)
    {
        app.MapPost("/conta/entrar", async ([FromForm] string email, [FromForm] string senha, [FromForm] string? codigo, [FromForm] string? retorno,
            HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            try
            {
                var u = await auth.ValidarCredenciaisAsync(email, senha, codigo, ct);
                await EntrarAsync(http, u.Claims);
                if (u.Usuario.DeveTrocarSenha) return Results.LocalRedirect("/trocar-senha");
                if (!string.IsNullOrEmpty(retorno) && Uri.IsWellFormedUriString(retorno, UriKind.Relative) && retorno.StartsWith('/') && !retorno.StartsWith("//"))
                    return Results.LocalRedirect(retorno);
                return Results.LocalRedirect(Inicio(u.Usuario.Papel));
            }
            catch (MfaNecessarioException)
            {
                return Results.LocalRedirect($"/entrar?mfa=1&email={Uri.EscapeDataString(email)}");
            }
            catch (AcessoNegadoException ex)
            {
                return Results.LocalRedirect($"/entrar?erro={Uri.EscapeDataString(ex.Message)}&email={Uri.EscapeDataString(email)}");
            }
        }).AllowAnonymous();

        app.MapPost("/conta/sair", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/entrar");
        });

        app.MapPost("/conta/senha", async ([FromForm] string atual, [FromForm] string nova, [FromForm] string confirmacao,
            HttpContext http, AuthService auth, PersonaliPontoDbContext db, RequestContext ctx, CancellationToken ct) =>
        {
            if (nova != confirmacao) return Results.LocalRedirect("/trocar-senha?erro=" + Uri.EscapeDataString("A confirmação não confere."));
            try
            {
                var id = http.User.UsuarioId()!.Value;
                await auth.TrocarSenhaAsync(id, atual, nova, ct);
                Usuario u;
                using (ctx.ComoSistema()) u = await db.Usuarios.AsNoTracking().FirstAsync(x => x.Id == id, ct);
                await EntrarAsync(http, AuthService.Claims(u));
                return Results.LocalRedirect(Inicio(u.Papel) + "?senha=ok");
            }
            catch (Exception ex) when (ex is RegraNegocioException or AcessoNegadoException)
            {
                return Results.LocalRedirect("/trocar-senha?erro=" + Uri.EscapeDataString(ex.Message));
            }
        }).RequireAuthorization();

        // Acesso de suporte da plataforma a um cliente: sempre com motivo e sempre registrado.
        app.MapPost("/plataforma/suporte/entrar", async ([FromForm] Guid tenantId, [FromForm] string motivo, HttpContext http,
            PersonaliPontoDbContext db, RequestContext ctx, IClock clock, AuditService audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
                return Results.LocalRedirect("/plataforma/clientes?erro=" + Uri.EscapeDataString("Informe o motivo do acesso (mínimo 10 caracteres)."));
            ctx.DefinirSistema();
            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
            if (t is null) return Results.NotFound();
            var acesso = new AcessoSuporte
            {
                TenantId = tenantId, UsuarioId = http.User.UsuarioId()!.Value, UsuarioNome = http.User.Identity!.Name ?? "",
                Motivo = motivo.Trim(), Inicio = clock.UtcNow, Ip = http.Connection.RemoteIpAddress?.ToString()
            };
            db.AcessosSuporte.Add(acesso);
            audit.Registrar("suporte.acesso_iniciado", nameof(Tenant), tenantId, new { motivo }, tenantId);
            await db.SaveChangesAsync(ct);

            var claims = http.User.Claims.Where(c => c.Type is not (PersonaliPontoClaims.TenantId or ClaimTypes.Role)).ToList();
            claims.Add(new Claim(ClaimTypes.Role, Roles.AdminEmpresa));
            claims.Add(new Claim(ClaimTypes.Role, http.User.FindFirst(ClaimTypes.Role)!.Value));
            claims.Add(new Claim(PersonaliPontoClaims.TenantId, tenantId.ToString()));
            claims.Add(new Claim("suporte_acesso", acesso.Id.ToString()));
            claims.Add(new Claim("suporte_cliente", t.Nome));
            await EntrarAsync(http, claims);
            return Results.LocalRedirect("/");
        }).RequireAuthorization(Politicas.Plataforma);

        app.MapPost("/plataforma/suporte/sair", async (HttpContext http, PersonaliPontoDbContext db, RequestContext ctx, IClock clock, CancellationToken ct) =>
        {
            ctx.DefinirSistema();
            if (Guid.TryParse(http.User.FindFirst("suporte_acesso")?.Value, out var id))
            {
                var a = await db.AcessosSuporte.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (a is not null) { a.Fim = clock.UtcNow; await db.SaveChangesAsync(ct); }
            }
            var u = await db.Usuarios.AsNoTracking().FirstAsync(x => x.Id == http.User.UsuarioId(), ct);
            await EntrarAsync(http, AuthService.Claims(u));
            return Results.LocalRedirect("/plataforma");
        }).RequireAuthorization();
    }

    public static string Inicio(string papel) => papel switch
    {
        Roles.SuperAdmin or Roles.Suporte => "/plataforma",
        Roles.Funcionario => "/meu-ponto",
        _ => "/"
    };

    private static Task EntrarAsync(HttpContext http, IEnumerable<Claim> claims) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role)),
            new AuthenticationProperties { IsPersistent = false });
}

public static class DownloadEndpoints
{
    public static void MapDownloadEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/download").RequireAuthorization();

        g.MapGet("/comprovante/{id:guid}", async (Guid id, HttpContext http, ComprovanteService svc, CancellationToken ct) =>
        {
            var somente = http.User.IsInRole(Roles.Funcionario) ? http.User.FuncionarioId() : null;
            if (http.User.IsInRole(Roles.Funcionario) && somente is null) return Results.Forbid();
            var (nome, pdf) = await svc.PdfAsync(id, somente, ct);
            return Results.File(pdf, "application/pdf", nome);
        });

        g.MapGet("/espelho", async (Guid? funcionarioId, DateOnly inicio, DateOnly fim, HttpContext http, EspelhoService svc, EspelhoPdfRenderer pdf, CancellationToken ct) =>
        {
            var alvo = Alvo(http, funcionarioId);
            if (alvo is null) return Results.Forbid();
            var dto = await svc.GerarAsync(alvo.Value, inicio, fim, ct);
            return Results.File(pdf.Renderizar(dto), "application/pdf", $"espelho-{Slug(dto.Funcionario)}-{inicio:yyyy-MM-dd}.pdf");
        });

        g.MapGet("/afd", async (Guid estabelecimentoId, DateOnly inicio, DateOnly fim, ArquivosFiscaisService svc, CancellationToken ct) =>
            Results.File((await svc.GerarAfdAsync(estabelecimentoId, inicio, fim, ct)) is var a ? a.Zip() : [], "application/zip", $"AFD-{inicio:yyyyMMdd}-{fim:yyyyMMdd}.zip"))
            .RequireAuthorization(Politicas.Rh);

        g.MapGet("/aej", async (Guid estabelecimentoId, DateOnly inicio, DateOnly fim, ArquivosFiscaisService svc, CancellationToken ct) =>
            Results.File((await svc.GerarAejAsync(estabelecimentoId, inicio, fim, ct)) is var a ? a.Zip() : [], "application/zip", $"AEJ-{inicio:yyyyMMdd}-{fim:yyyyMMdd}.zip"))
            .RequireAuthorization(Politicas.Rh);

        g.MapGet("/relatorio/{tipo}", async (string tipo, DateOnly? inicio, DateOnly? fim, Guid? estabelecimentoId, AnalyticsService svc, CancellationToken ct) =>
        {
            var ini = inicio ?? new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
            var f = fim ?? DateOnly.FromDateTime(DateTime.Today);
            var bytes = tipo switch
            {
                "resumo" => await svc.RelatorioResumoXlsxAsync(ini, f, estabelecimentoId, ct),
                "marcacoes" => await svc.RelatorioMarcacoesXlsxAsync(ini, f, estabelecimentoId, ct),
                "banco-horas" => await svc.RelatorioBancoHorasXlsxAsync(ct),
                _ => null
            };
            return bytes is null ? Results.NotFound()
                : Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{tipo}-{ini:yyyyMMdd}-{f:yyyyMMdd}.xlsx");
        }).RequireAuthorization(Politicas.Empresa);

        g.MapGet("/arquivo/{id:guid}", async (Guid id, HttpContext http, ArquivoService arquivos, PersonaliPontoDbContext db, CancellationToken ct) =>
        {
            // Funcionário só baixa arquivos dos próprios atestados.
            if (http.User.IsInRole(Roles.Funcionario))
            {
                var f = http.User.FuncionarioId();
                if (!await db.Atestados.AnyAsync(a => a.ArquivoId == id && a.FuncionarioId == f, ct)) return Results.NotFound();
            }
            var r = await arquivos.ObterAsync(id, ct);
            return r is null ? Results.NotFound() : Results.File(r.Value.Conteudo, r.Value.Info.ContentType, r.Value.Info.NomeOriginal);
        });

        g.MapGet("/plataforma/faturamento", async (int ano, PainelPlataformaService painel, RequestContext ctx, CancellationToken ct) =>
        {
            ctx.DefinirSistema();
            var grade = await painel.GradeAsync(ano, null, null, ct);
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.AddWorksheet($"Faturamento {ano}");
            string[] cab = ["Razão social", "CNPJ", "Telefone", "Status", "Valor", "Funcionários", "Custo", "Fornecedor"];
            for (var i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
            for (var m = 0; m < 12; m++) ws.Cell(1, cab.Length + 1 + m).Value = new DateTime(ano, m + 1, 1).ToString("MMM/yy", new System.Globalization.CultureInfo("pt-BR"));
            ws.Cell(1, cab.Length + 13).Value = "Em aberto (vencido)";
            ws.Row(1).Style.Font.SetBold().Font.SetFontColor(ClosedXML.Excel.XLColor.White).Fill.SetBackgroundColor(ClosedXML.Excel.XLColor.FromHtml("#082352"));
            var l = 2;
            foreach (var r in grade)
            {
                ws.Cell(l, 1).Value = r.RazaoSocial; ws.Cell(l, 2).Value = r.Cnpj; ws.Cell(l, 3).Value = r.Telefone; ws.Cell(l, 4).Value = r.Status.ToString();
                ws.Cell(l, 5).Value = r.Valor; ws.Cell(l, 6).Value = r.Funcionarios; ws.Cell(l, 7).Value = r.Custo; ws.Cell(l, 8).Value = r.Fornecedor;
                var m = 0;
                foreach (var (_, (estado, _)) in r.Meses.OrderBy(x => x.Key))
                    ws.Cell(l, cab.Length + 1 + m++).Value = estado switch
                    {
                        CelulaFatura.Paga => "PAGO", CelulaFatura.NaoCobrada => "XXXXX", CelulaFatura.Vencida => "VENCIDO",
                        CelulaFatura.Aberta => "ABERTO", CelulaFatura.Cancelada => "CANCELADO", _ => ""
                    };
                ws.Cell(l, cab.Length + 13).Value = r.EmAberto;
                l++;
            }
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"faturamento-{ano}.xlsx");
        }).RequireAuthorization(Politicas.Plataforma);
    }

    private static Guid? Alvo(HttpContext http, Guid? funcionarioId)
    {
        if (http.User.IsInRole(Roles.AdminEmpresa) || http.User.IsInRole(Roles.RH) || http.User.IsInRole(Roles.Gestor))
            return funcionarioId ?? http.User.FuncionarioId();
        var proprio = http.User.FuncionarioId();
        return funcionarioId is null || funcionarioId == proprio ? proprio : null;
    }

    private static string Slug(string s) => ClienteService.Slug(s);
}
