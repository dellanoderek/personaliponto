using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.RH.Domain;
using PersonaliPonto.Modules.RH.Services;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Hosting;

/// <summary>
/// Dados fictícios para demonstração e testes visuais. SOMENTE em ambiente de desenvolvimento:
/// as marcações históricas são gravadas como off-line e não têm valor legal.
/// </summary>
public static class DemoSeeder
{
    private static readonly string[] Nomes =
    [
        "Ana Beatriz Souza", "Bruno Henrique Lima", "Carla Menezes", "Diego Albuquerque", "Elaine Cristina Rocha",
        "Fábio Nascimento", "Gabriela Torres", "Heitor Barbosa"
    ];

    public static async Task<string> CriarAsync(IServiceProvider root, CancellationToken ct = default)
    {
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        var ctx = sp.GetRequiredService<RequestContext>();
        ctx.DefinirSistema();
        ctx.DefinirUsuario(null, "Demonstração", null, Roles.SuperAdmin, null);
        var db = sp.GetRequiredService<PersonaliPontoDbContext>();

        var baseCnpj = Random.Shared.Next(10000000, 99999999).ToString() + "0001";
        var cnpj = baseCnpj + PersonaliPonto.Core.RepP.Formatacao.Documentos.DigitosCnpj(baseCnpj);
        var email = $"rh+{cnpj[..6]}@demo.test";
        var cliente = await sp.GetRequiredService<ClienteService>().CadastrarAsync(new NovoClienteRequest(
            "EMPRESA DEMONSTRAÇÃO LTDA", cnpj, "83 90000-0000", email, "RH Demonstração", null, 249.90m, 50, 60.60m,
            "PERSONALIPONTO", 10, null, "Av. Floriano Peixoto, 1000 - Centro, Campina Grande/PB", false), ct);

        ctx.DefinirTenant(cliente.TenantId);
        var cad = sp.GetRequiredService<CadastroService>();
        var est = await db.Estabelecimentos.FirstAsync(ct);
        est.Latitude = -7.2306; est.Longitude = -35.8811; est.RaioCercaMetros = 300; est.ModoCerca = ModoCerca.Sinalizar;
        await db.SaveChangesAsync(ct);
        var jornada = await cad.SalvarJornadaAsync(CadastroService.Modelo(TipoJornada.Semanal5x2, "COM44", "Comercial 44h (5x2)"), ct);
        await cad.SalvarJornadaAsync(CadastroService.Modelo(TipoJornada.Escala12x36, "E1236", "Escala 12x36 diurna"), ct);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        await sp.GetRequiredService<FeriadoService>().GerarNacionaisAsync(hoje.Year, ct);

        var funcs = new List<Funcionario>();
        for (var i = 0; i < Nomes.Length; i++)
        {
            var nove = Random.Shared.Next(100000000, 999999999).ToString();
            var cpf = CpfCom(nove);
            var f = await cad.SalvarFuncionarioAsync(new Funcionario
            {
                EstabelecimentoId = est.Id, Nome = Nomes[i], Cpf = cpf, Matricula = (101 + i).ToString(), DataAdmissao = hoje.AddMonths(-6),
                Cargo = i % 3 == 0 ? "Atendente" : i % 3 == 1 ? "Auxiliar administrativo" : "Operador de caixa", JornadaId = jornada.Id, Ativo = true,
                PermiteMarcacaoApp = true, PermiteMarcacaoOffline = true
            }, ct);
            await cad.DefinirPinAsync(f.Id, "4821", ct);
            funcs.Add(f);
        }

        // Três semanas de marcações com variações realistas (atrasos, extras, esquecimentos).
        var reg = sp.GetRequiredService<RegistroRepService>();
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(est.FusoHorario);
        var rnd = new Random(42);
        for (var d = hoje.AddDays(-21); d < hoje; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            foreach (var f in funcs)
            {
                if (rnd.NextDouble() < 0.04) continue; // falta
                var atraso = rnd.NextDouble() < 0.15 ? rnd.Next(12, 45) : rnd.Next(-6, 6);
                var extra = rnd.NextDouble() < 0.2 ? rnd.Next(20, 110) : rnd.Next(-4, 6);
                var horarios = new List<TimeOnly> { new TimeOnly(8, 0).AddMinutes(atraso), new TimeOnly(12, 0).AddMinutes(rnd.Next(-3, 4)),
                    new TimeOnly(13, 0).AddMinutes(rnd.Next(-3, 6)), new TimeOnly(17, 48).AddMinutes(extra) };
                if (rnd.NextDouble() < 0.05) horarios.RemoveAt(2); // esqueceu uma marcação
                foreach (var h in horarios)
                {
                    var local = d.ToDateTime(h);
                    await reg.RegistrarMarcacaoAsync(cliente.TenantId, est.Id, f, new DateTimeOffset(local, fuso.GetUtcOffset(local)),
                        rnd.NextDouble() < 0.6 ? Coletor.AplicativoMobile : Coletor.Browser, true, Guid.NewGuid(), ct);
                }
            }
        }

        var sol = sp.GetRequiredService<SolicitacaoService>();
        await sol.SolicitarAsync(funcs[1].Id, TipoSolicitacao.Inclusao, hoje.AddDays(-2), new TimeOnly(13, 0), null, "Esqueci de marcar a volta do almoço", ct);
        await sol.SolicitarAsync(funcs[4].Id, TipoSolicitacao.Inclusao, hoje.AddDays(-5), new TimeOnly(17, 50), null, "Sistema estava fora do ar na saída", ct);
        await sp.GetRequiredService<AtestadoService>().EnviarAsync(funcs[2].Id, TipoAtestado.DeclaracaoComparecimento, hoje.AddDays(-3), hoje.AddDays(-3),
            null, "Consulta médica pela manhã", null, null, ct);

        var acesso = await sp.GetRequiredService<UsuarioService>().CriarAcessoFuncionarioAsync(funcs[0].Id, ct);
        return $"Cliente demo criado. RH: {email} / {cliente.SenhaTemporaria} · Funcionário: {acesso.Login} / {acesso.SenhaTemporaria} · PIN terminal: 4821 (matrículas 101 a 108)";
    }

    private static string CpfCom(string nove)
    {
        int Dv(string s, int peso)
        {
            var soma = 0;
            foreach (var c in s) soma += (c - '0') * peso--;
            var r = soma % 11;
            return r < 2 ? 0 : 11 - r;
        }
        var d1 = Dv(nove, 10);
        return nove + d1 + Dv(nove + d1, 11);
    }
}
