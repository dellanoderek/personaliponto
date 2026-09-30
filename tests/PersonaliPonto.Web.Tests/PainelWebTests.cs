using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonaliPonto.Core.RepP.Afd;

namespace PersonaliPonto.Web.Tests;

[Collection("web")]
public sealed class PainelWebTests(WebFixture fx)
{
    public static readonly TheoryData<string, string> PaginasRh = new()
    {
        { "/", "Saúde da operação" },
        { "/marcacoes", "Marcações registradas" },
        { "/espelho", "Espelho de ponto" },
        { "/solicitacoes", "Solicitações de ajuste" },
        { "/atestados", "Atestados e justificativas" },
        { "/banco-horas", "Banco de horas" },
        { "/fechamento", "Fechamento mensal" },
        { "/funcionarios", "Funcionários" },
        { "/jornadas", "Jornadas e escalas" },
        { "/estabelecimentos", "Empresa e locais" },
        { "/feriados", "Feriados e calendário" },
        { "/relatorios", "Relatórios" },
        { "/arquivos-fiscais", "Arquivos fiscais" },
        { "/auditoria", "Trilha de auditoria" },
        { "/seguranca", "Segurança e antifraude" },
        { "/terminais", "Terminais de ponto" },
        { "/usuarios", "Usuários e perfis" },
        { "/minha-assinatura", "Minha assinatura" },
        { "/minha-conta", "Minha conta" }
    };

    [Theory]
    [MemberData(nameof(PaginasRh))]
    public async Task Paginas_do_RH_renderizam(string url, string titulo)
    {
        var c = fx.Cliente();
        Assert.Equal("/", await WebFixture.EntrarAsync(c, WebFixture.EmailRh, WebFixture.SenhaTeste));
        var r = await c.GetAsync(url);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(titulo, html);
        Assert.DoesNotContain("Algo deu errado", html);
    }

    public static readonly TheoryData<string, string> PaginasPlataforma = new()
    {
        { "/plataforma", "Visão geral da plataforma" },
        { "/plataforma/clientes", "EMPRESA TESTE WEB LTDA" },
        { "/plataforma/faturamento", "Faturamento" },
        { "/plataforma/importar", "Importar planilha" },
        { "/plataforma/planos", "Profissional" },
        { "/plataforma/acessos", "Acessos de suporte" }
    };

    [Theory]
    [MemberData(nameof(PaginasPlataforma))]
    public async Task Paginas_do_super_admin_renderizam(string url, string texto)
    {
        var c = fx.Cliente();
        Assert.Equal("/plataforma", await WebFixture.EntrarAsync(c, WebFixture.EmailAdmin, WebFixture.SenhaTeste));
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(texto, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RH_nao_acessa_plataforma_e_anonimo_vai_para_login()
    {
        var anon = fx.Cliente();
        var r0 = await anon.GetAsync("/funcionarios");
        Assert.Equal(HttpStatusCode.Redirect, r0.StatusCode);
        Assert.Contains("/entrar", r0.Headers.Location!.ToString());

        var c = fx.Cliente();
        await WebFixture.EntrarAsync(c, WebFixture.EmailRh, WebFixture.SenhaTeste);
        var negado = await c.GetAsync("/plataforma/clientes");
        Assert.Equal(HttpStatusCode.Redirect, negado.StatusCode);
        Assert.StartsWith("/acesso-negado", negado.Headers.Location!.PathAndQuery());
    }

    [Fact]
    public async Task Login_invalido_volta_com_erro()
    {
        var c = fx.Cliente();
        var destino = await WebFixture.EntrarAsync(c, WebFixture.EmailRh, "errada");
        Assert.StartsWith("/entrar?erro=", destino);
    }

    [Fact]
    public async Task Funcionario_entra_por_CPF_ve_meu_ponto_e_nao_ve_o_RH()
    {
        var c = fx.Cliente();
        Assert.Equal("/meu-ponto", await WebFixture.EntrarAsync(c, fx.CpfFuncionario, WebFixture.SenhaTeste));
        var r = await c.GetAsync("/meu-ponto");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Registrar ponto", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        var negado = await c.GetAsync("/funcionarios");
        Assert.Equal(HttpStatusCode.Redirect, negado.StatusCode);
        Assert.StartsWith("/acesso-negado", negado.Headers.Location!.PathAndQuery());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/meu-ponto/espelho")).StatusCode);
    }

    [Fact]
    public async Task Terminal_ativado_marca_ponto_com_PIN_e_gera_AFD_valido()
    {
        var c = fx.Cliente();
        await WebFixture.EntrarAsync(c, WebFixture.EmailRh, WebFixture.SenhaTeste);
        var token = await WebFixture.TokenAsync(c, "/terminais");
        var ativ = await c.PostAsync("/terminal/ativar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["estabelecimentoId"] = fx.EstabelecimentoId.ToString(), ["nome"] = "Recepção"
        }));
        Assert.Equal(HttpStatusCode.Redirect, ativ.StatusCode);
        Assert.Equal("/terminal", ativ.Headers.Location!.ToString());

        var info = await c.GetFromJsonAsync<JsonElement>("/terminal/api/info");
        Assert.Equal("Recepção", info.GetProperty("nome").GetString());
        Assert.False(string.IsNullOrEmpty(info.GetProperty("chavePublica").GetString()));

        // Sem o cabeçalho anti-CSRF a requisição é recusada.
        var semCabecalho = await c.PostAsJsonAsync("/terminal/api/marcacoes", new { clientId = Guid.NewGuid(), identificacao = "501", pin = "4821" });
        Assert.Equal(HttpStatusCode.BadRequest, semCabecalho.StatusCode);

        async Task<HttpResponseMessage> Marcar(string pin, Guid? id = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/terminal/api/marcacoes")
            {
                Content = JsonContent.Create(new { clientId = id ?? Guid.NewGuid(), identificacao = "501", pin, offline = false })
            };
            req.Headers.Add("X-PersonaliPonto-Terminal", "1");
            return await c.SendAsync(req);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Marcar("1111")).StatusCode);
        var ok = await Marcar("4821");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var corpo = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Funcionária Teste", corpo.GetProperty("funcionarioNome").GetString());
        Assert.Equal(64, corpo.GetProperty("hash").GetString()!.Length);

        // Comprovante, espelho e AFD pelo painel.
        var id = corpo.GetProperty("id").GetGuid();
        var pdf = await c.GetAsync($"/download/comprovante/{id}");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var hoje = DateTime.Today;
        var zip = await c.GetAsync($"/download/afd?estabelecimentoId={fx.EstabelecimentoId}&inicio={hoje.AddDays(-1):yyyy-MM-dd}&fim={hoje.AddDays(1):yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, zip.StatusCode);
        using var arq = new System.IO.Compression.ZipArchive(await zip.Content.ReadAsStreamAsync());
        var txt = arq.Entries.Single(e => e.Name.EndsWith(".txt"));
        Assert.Contains(arq.Entries, e => e.Name == txt.Name + ".p7s");
        using var ms = new MemoryStream();
        await txt.Open().CopyToAsync(ms);
        Assert.Empty(AfdValidator.Validar(ms.ToArray()));

        var esp = await c.GetAsync($"/download/espelho?funcionarioId={fx.FuncionarioId}&inicio={hoje:yyyy-MM-01}&fim={hoje:yyyy-MM-dd}");
        Assert.Equal("application/pdf", esp.Content.Headers.ContentType!.MediaType);
        var xlsx = await c.GetAsync($"/download/relatorio/resumo?inicio={hoje:yyyy-MM-01}&fim={hoje:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var aej = await c.GetAsync($"/download/aej?estabelecimentoId={fx.EstabelecimentoId}&inicio={hoje:yyyy-MM-01}&fim={hoje:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, aej.StatusCode);
    }

    [Fact]
    public async Task Terminal_sem_ativacao_nao_marca()
    {
        var c = fx.Cliente();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/terminal/api/info")).StatusCode);
        var pagina = await c.GetAsync("/terminal");
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains("Terminal não ativado", WebUtility.HtmlDecode(await pagina.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Funcionario_nao_baixa_comprovante_de_outro()
    {
        var c = fx.Cliente();
        await WebFixture.EntrarAsync(c, fx.CpfFuncionario, WebFixture.SenhaTeste);
        var r = await c.GetAsync($"/download/comprovante/{Guid.NewGuid()}");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        var afd = await c.GetAsync($"/download/afd?estabelecimentoId={fx.EstabelecimentoId}&inicio=2026-01-01&fim=2026-01-31");
        Assert.Equal(HttpStatusCode.Redirect, afd.StatusCode);
        Assert.StartsWith("/acesso-negado", afd.Headers.Location!.PathAndQuery());
    }
}

internal static class UriExt
{
    public static string PathAndQuery(this Uri u) => u.IsAbsoluteUri ? u.PathAndQuery : u.OriginalString;
}
