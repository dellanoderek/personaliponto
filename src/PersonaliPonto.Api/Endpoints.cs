using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Documentos;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Modules.RH.Domain;
using PersonaliPonto.Modules.RH.Services;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Api;

public static class AuthEndpoints
{
    public sealed record TrocarSenhaRequest(string SenhaAtual, string NovaSenha);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (LoginRequest r, AuthService auth, CancellationToken ct) =>
        {
            var u = await auth.ValidarCredenciaisAsync(r.Email, r.Senha, r.CodigoMfa, ct);
            return Results.Ok(await auth.EmitirTokensAsync(u.Usuario, ct));
        }).RequireRateLimiting("login").AllowAnonymous();

        g.MapPost("/refresh", async (RefreshRequest r, AuthService auth, CancellationToken ct) =>
            Results.Ok(await auth.RenovarAsync(r.RefreshToken, ct))).RequireRateLimiting("login").AllowAnonymous();

        g.MapPost("/logout", async (RefreshRequest r, AuthService auth, CancellationToken ct) =>
        {
            await auth.SairAsync(r.RefreshToken, ct);
            return Results.NoContent();
        }).AllowAnonymous();

        g.MapPost("/senha", async (TrocarSenhaRequest r, ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
        {
            await auth.TrocarSenhaAsync(user.UsuarioId()!.Value, r.SenhaAtual, r.NovaSenha, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}

public static class FuncionarioEndpoints
{
    public sealed record PerfilDto(string Nome, string Cpf, string Matricula, string? Cargo, string Empresa, string Estabelecimento,
        string? Jornada, bool PermiteOffline, bool DeveTrocarSenha, int SaldoBancoHoras, string FusoHorario, bool FotoNaMarcacao);

    public sealed record AssinarEspelhoRequest(int Ano, int Mes, bool Concorda, string? Observacao);

    public sealed record SolicitacaoRequest(TipoSolicitacao Tipo, DateOnly Data, TimeOnly? Horario, Guid? RegistroRepId, string Motivo);

    public static void MapFuncionarioEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/app").RequireAuthorization("Funcionario");

        g.MapGet("/perfil", async (ClaimsPrincipal user, PersonaliPontoDbContext db, BancoHorasService bh, ConfiguracaoSegurancaService seg, CancellationToken ct) =>
        {
            var id = user.FuncionarioId()!.Value;
            var f = await db.Funcionarios.AsNoTracking().Include(x => x.Estabelecimento).ThenInclude(e => e!.Empregador).Include(x => x.Jornada)
                .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Funcionário não encontrado.");
            return Results.Ok(new PerfilDto(f.Nome, PersonaliPonto.Core.RepP.Formatacao.Documentos.FormatarCpf(f.Cpf), f.Matricula, f.Cargo,
                f.Estabelecimento!.Empregador!.RazaoSocial, f.Estabelecimento.Nome, f.Jornada?.Nome, f.PermiteMarcacaoOffline,
                user.HasClaim(c => c.Type == "trocar_senha"), await bh.SaldoAsync(id, null, ct), f.Estabelecimento.FusoHorario,
                (await seg.ObterAsync(ct)).FotoNaMarcacao));
        });

        // Hora oficial + âncora para marcações off-line (o app nunca usa o relógio do aparelho como fonte).
        g.MapGet("/tempo", async (ClaimsPrincipal user, PersonaliPontoDbContext db, MarcacaoService svc, CancellationToken ct) =>
        {
            var id = user.FuncionarioId()!.Value;
            var f = await db.Funcionarios.AsNoTracking().FirstAsync(x => x.Id == id, ct);
            return Results.Ok(await svc.EmitirAncoraAsync(f.TenantId, f.EstabelecimentoId, f.Id, null, ct));
        });

        g.MapPost("/marcacoes", async (RegistrarMarcacaoRequest r, ClaimsPrincipal user, HttpContext http, PersonaliPontoDbContext db, MarcacaoService svc, CancellationToken ct) =>
        {
            var id = user.FuncionarioId()!.Value;
            var f = await db.Funcionarios.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Funcionário não encontrado.");
            var res = await svc.RegistrarAsync(f, r, Coletor.AplicativoMobile, null, http.Connection.RemoteIpAddress?.ToString(), ct);
            return res.Duplicada ? Results.Ok(res) : Results.Created($"/api/app/comprovantes/{res.Id}", res);
        }).RequireRateLimiting("marcacao");

        // Foto opcional da marcação (Fase 5): somente quando habilitada pela empresa após avaliação de LGPD.
        g.MapPost("/marcacoes/{id:guid}/foto", async (Guid id, IFormFile foto, ClaimsPrincipal user, FotoMarcacaoService fotos, CancellationToken ct) =>
        {
            if (foto.Length > ArquivoService.TamanhoMaximo) throw new RegraNegocioException("Foto acima de 10 MB.");
            using var ms = new MemoryStream();
            await foto.CopyToAsync(ms, ct);
            await fotos.AnexarAsync(id, user.FuncionarioId()!.Value, ms.ToArray(), ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        g.MapGet("/marcacoes", async (ClaimsPrincipal user, MarcacaoService svc, int? quantidade, CancellationToken ct) =>
            Results.Ok(await svc.UltimasAsync(user.FuncionarioId()!.Value, Math.Clamp(quantidade ?? 20, 1, 100), ct)));

        g.MapGet("/comprovantes/{id:guid}", async (Guid id, ClaimsPrincipal user, ComprovanteService svc, CancellationToken ct) =>
        {
            var (nome, pdf) = await svc.PdfAsync(id, user.FuncionarioId()!.Value, ct);
            return Results.File(pdf, "application/pdf", nome);
        });

        g.MapGet("/espelho", async (DateOnly inicio, DateOnly fim, ClaimsPrincipal user, EspelhoService svc, CancellationToken ct) =>
            Results.Ok(await svc.GerarAsync(user.FuncionarioId()!.Value, inicio, fim, ct)));

        g.MapGet("/espelho/pdf", async (DateOnly inicio, DateOnly fim, ClaimsPrincipal user, EspelhoService svc, EspelhoPdfRenderer pdf, CancellationToken ct) =>
        {
            var dto = await svc.GerarAsync(user.FuncionarioId()!.Value, inicio, fim, ct);
            return Results.File(pdf.Renderizar(dto), "application/pdf", $"espelho-{inicio:yyyy-MM}.pdf");
        });

        g.MapGet("/espelho/assinatura", async (int ano, int mes, ClaimsPrincipal user, AssinaturaEspelhoService svc, CancellationToken ct) =>
        {
            var a = await svc.ObterAsync(user.FuncionarioId()!.Value, ano, mes, ct);
            return a is null ? Results.NoContent() : Results.Ok(new { a.AssinadoEm, a.Concorda, a.Observacao, a.HashEspelho });
        });

        g.MapPost("/espelho/assinatura", async (AssinarEspelhoRequest r, ClaimsPrincipal user, AssinaturaEspelhoService svc, CancellationToken ct) =>
        {
            var a = await svc.AssinarAsync(user.FuncionarioId()!.Value, r.Ano, r.Mes, r.Concorda, r.Observacao, ct);
            return Results.Ok(new { a.AssinadoEm, a.HashEspelho });
        });

        g.MapGet("/solicitacoes", async (ClaimsPrincipal user, PersonaliPontoDbContext db, CancellationToken ct) =>
        {
            var id = user.FuncionarioId()!.Value;
            var lista = await db.Solicitacoes.AsNoTracking().Where(s => s.FuncionarioId == id).OrderByDescending(s => s.CriadaEm).Take(100)
                .Select(s => new SolicitacaoDto(s.Id, s.Data, s.Horario ?? TimeOnly.MinValue, s.Motivo, s.Status.ToString(), s.RespostaRh, s.CriadaEm))
                .ToListAsync(ct);
            return Results.Ok(lista);
        });

        g.MapPost("/solicitacoes", async (SolicitacaoRequest r, ClaimsPrincipal user, SolicitacaoService svc, TenantUsoPolicy uso, CancellationToken ct) =>
        {
            await GarantirUsoAsync(user, uso, ct);
            var s = await svc.SolicitarAsync(user.FuncionarioId()!.Value, r.Tipo, r.Data, r.Horario, r.RegistroRepId, r.Motivo, ct);
            return Results.Created($"/api/app/solicitacoes/{s.Id}", new { s.Id });
        });

        g.MapDelete("/solicitacoes/{id:guid}", async (Guid id, ClaimsPrincipal user, SolicitacaoService svc, CancellationToken ct) =>
        {
            await svc.CancelarAsync(id, user.FuncionarioId()!.Value, ct);
            return Results.NoContent();
        });

        g.MapPost("/atestados", async ([FromForm] TipoAtestado tipo, [FromForm] DateOnly inicio, [FromForm] DateOnly fim, [FromForm] string? observacao,
            IFormFile? arquivo, ClaimsPrincipal user, AtestadoService svc, TenantUsoPolicy uso, CancellationToken ct) =>
        {
            await GarantirUsoAsync(user, uso, ct);
            byte[]? bytes = null;
            if (arquivo is not null)
            {
                if (arquivo.Length > ArquivoService.TamanhoMaximo) throw new RegraNegocioException("Arquivo acima de 10 MB.");
                using var ms = new MemoryStream();
                await arquivo.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }
            var a = await svc.EnviarAsync(user.FuncionarioId()!.Value, tipo, inicio, fim, null, observacao, arquivo?.FileName, bytes, ct);
            return Results.Created($"/api/app/atestados/{a.Id}", new { a.Id });
        }).DisableAntiforgery();

        g.MapGet("/atestados", async (ClaimsPrincipal user, PersonaliPontoDbContext db, CancellationToken ct) =>
        {
            var id = user.FuncionarioId()!.Value;
            return Results.Ok(await db.Atestados.AsNoTracking().Where(a => a.FuncionarioId == id).OrderByDescending(a => a.CriadoEm).Take(50)
                .Select(a => new { a.Id, Tipo = a.Tipo.ToString(), a.DataInicio, a.DataFim, Status = a.Status.ToString(), a.ParecerRh }).ToListAsync(ct));
        });

        g.MapGet("/banco-horas", async (ClaimsPrincipal user, BancoHorasService bh, CancellationToken ct) =>
        {
            var id = user.FuncionarioId()!.Value;
            var extrato = await bh.ExtratoAsync(id, ct);
            return Results.Ok(new
            {
                Saldo = extrato.Sum(l => l.Minutos),
                Lancamentos = extrato.Take(100).Select(l => new { l.Data, l.Minutos, Tipo = l.Tipo.ToString(), l.Descricao })
            });
        });
    }

    private static async Task GarantirUsoAsync(ClaimsPrincipal user, TenantUsoPolicy uso, CancellationToken ct)
    {
        var (pode, msg) = await uso.PodeUsarAsync(user.TenantId()!.Value, ct);
        if (!pode) throw new RegraNegocioException(msg!);
    }
}
