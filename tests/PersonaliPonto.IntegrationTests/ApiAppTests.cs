using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.IntegrationTests;

/// <summary>Fluxo do aplicativo do funcionário pela API real (JWT, marcação on-line/off-line, comprovante, espelho).</summary>
[Collection("banco")]
public sealed class ApiAppTests(BancoFixture fx) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _api = null!;
    private string _cpf = "";
    private const string Senha = "SenhaDoApp2026";

    public async Task InitializeAsync()
    {
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:PersonaliPonto", fx.ConnectionString);
            b.UseSetting("Rotinas:Desabilitar", "true");
            b.UseSetting("Jwt:Chave", new string('x', 48));
            b.UseSetting("RepP:NumeroRegistroInpi", "BR512026000123-4");
            b.UseSetting("Storage:DiretorioLocal", Path.Combine(Path.GetTempPath(), "tc-api-" + Guid.NewGuid().ToString("N")[..6]));
        });

        _cpf = Docs.Cpf();
        var c = await fx.CriarCenarioAsync("App", Docs.Cnpj(), _cpf);
        await using var s = fx.Escopo(c.TenantId, papel: Roles.RH);
        var acesso = await s.ServiceProvider.GetRequiredService<UsuarioService>().CriarAcessoFuncionarioAsync(c.FuncionarioId, default);
        await using var sis = fx.Escopo(sistema: true);
        var db = sis.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var u = await db.Usuarios.FirstAsync(x => x.Id == acesso.UsuarioId);
        u.SenhaHash = SecretHasher.Hash(Senha);
        u.DeveTrocarSenha = false;
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<(HttpClient Cliente, TokenResponse Tokens)> LoginAsync()
    {
        var c = _api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var r = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(_cpf, Senha));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var t = (await r.Content.ReadFromJsonAsync<TokenResponse>())!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t.AccessToken);
        return (c, t);
    }

    [Fact]
    public async Task Fluxo_completo_do_aplicativo()
    {
        var (c, tokens) = await LoginAsync();

        var perfil = await c.GetFromJsonAsync<JsonElement>("/api/app/perfil");
        Assert.Contains("Funcionário App", perfil.GetProperty("nome").GetString());

        // Marcação on-line: horário decidido pelo servidor.
        var antes = DateTimeOffset.UtcNow.AddSeconds(-2);
        var r1 = await c.PostAsJsonAsync("/api/app/marcacoes", new RegistrarMarcacaoRequest(Guid.NewGuid(), false, DateTimeOffset.UtcNow.AddHours(-5), null, null, null, "cel-1"));
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var m1 = (await r1.Content.ReadFromJsonAsync<MarcacaoRegistradaDto>())!;
        Assert.True(m1.DataHoraMarcacao >= antes, "O horário informado pelo aparelho foi ignorado nas marcações on-line.");
        Assert.False(m1.Offline);

        // Off-line com âncora do servidor.
        var ancora = (await c.GetFromJsonAsync<AncoraHoraDto>("/api/app/tempo"))!;
        await Task.Delay(1100);
        var idOff = Guid.NewGuid();
        var r2 = await c.PostAsJsonAsync("/api/app/marcacoes", new RegistrarMarcacaoRequest(idOff, true, ancora.HoraServidor.AddSeconds(1), ancora.AncoraId, null, null, "cel-1"));
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.True((await r2.Content.ReadFromJsonAsync<MarcacaoRegistradaDto>())!.Offline);

        // Reenvio (idempotência) devolve 200 com a mesma marcação.
        var r3 = await c.PostAsJsonAsync("/api/app/marcacoes", new RegistrarMarcacaoRequest(idOff, true, ancora.HoraServidor.AddSeconds(1), ancora.AncoraId, null, null, "cel-1"));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        Assert.True((await r3.Content.ReadFromJsonAsync<MarcacaoRegistradaDto>())!.Duplicada);

        // Off-line forjado: conflito.
        var r4 = await c.PostAsJsonAsync("/api/app/marcacoes", new RegistrarMarcacaoRequest(Guid.NewGuid(), true, ancora.HoraServidor.AddDays(-3), ancora.AncoraId, null, null, "cel-1"));
        Assert.Equal(HttpStatusCode.Conflict, r4.StatusCode);

        var ultimas = await c.GetFromJsonAsync<List<UltimaMarcacaoDto>>("/api/app/marcacoes");
        Assert.Equal(2, ultimas!.Count);

        var pdf = await c.GetAsync($"/api/app/comprovantes/{m1.Id}");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var esp = await c.GetFromJsonAsync<EspelhoDto>($"/api/app/espelho?inicio={hoje.AddDays(-1):yyyy-MM-dd}&fim={hoje.AddDays(1):yyyy-MM-dd}");
        Assert.Equal(2, esp!.Dias.SelectMany(d => d.Marcacoes).Count());

        var sol = await c.PostAsJsonAsync("/api/app/solicitacoes", new { tipo = 1, data = hoje.AddDays(-1), horario = "12:00", motivo = "Esqueci de marcar a saída" });
        Assert.Equal(HttpStatusCode.Created, sol.StatusCode);
        var inval = await c.PostAsJsonAsync("/api/app/solicitacoes", new { tipo = 1, data = hoje.AddDays(-1), horario = "12:00", motivo = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inval.StatusCode);

        // Refresh token com rotação.
        var anon = _api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var novo = await anon.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, novo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task Sem_token_ou_com_perfil_errado_nao_acessa()
    {
        var anon = _api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/app/perfil")).StatusCode);
        var falha = await anon.PostAsJsonAsync("/api/auth/login", new LoginRequest(_cpf, "errada"));
        Assert.Equal(HttpStatusCode.Unauthorized, falha.StatusCode);
        var erro = await falha.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("E-mail ou senha inválidos.", erro.GetProperty("title").GetString());
    }
}
