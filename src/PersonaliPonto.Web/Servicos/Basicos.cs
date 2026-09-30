using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Seguranca;

namespace PersonaliPonto.Web.Servicos;

public static class Politicas
{
    public const string Empresa = "empresa";
    public const string Rh = "rh";
    public const string Admin = "admin";
    public const string Plataforma = "plataforma";
    public const string Funcionario = "funcionario";
}

public sealed record Aviso(Guid Id, string Texto, string Tipo);

/// <summary>Notificações (toasts) do circuito.</summary>
public sealed class Notificacoes
{
    private readonly List<Aviso> _itens = [];
    public IReadOnlyList<Aviso> Itens => _itens;
    public event Action? Mudou;

    public void Sucesso(string texto) => Adicionar(texto, "sucesso");
    public void Erro(string texto) => Adicionar(texto, "erro");
    public void Info(string texto) => Adicionar(texto, "");

    private void Adicionar(string texto, string tipo)
    {
        var a = new Aviso(Guid.NewGuid(), texto, tipo);
        _itens.Add(a);
        Mudou?.Invoke();
        _ = Task.Delay(tipo == "erro" ? 7000 : 4000).ContinueWith(_ => Remover(a.Id));
    }

    public void Remover(Guid id)
    {
        _itens.RemoveAll(x => x.Id == id);
        Mudou?.Invoke();
    }

    /// <summary>Mensagem amigável para exceções de domínio; erros inesperados viram mensagem genérica.</summary>
    public static string Mensagem(Exception ex) => ex switch
    {
        RegraNegocioException or AcessoNegadoException or NaoEncontradoException or MfaNecessarioException => ex.Message,
        UnauthorizedAccessException => "Acesso negado.",
        _ => "Não foi possível concluir a operação. Tente novamente."
    };
}

public static class Formato
{
    public static string Hhmm(int minutos) => EspelhoService.Hhmm(minutos);

    public static string Moeda(decimal v) => v.ToString("C", new System.Globalization.CultureInfo("pt-BR"));

    public static string DiaSemana(DateOnly d) => new System.Globalization.CultureInfo("pt-BR").DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.');

    public static string Iniciais(string? nome)
    {
        var p = (nome ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => char.IsLetter(x[0])).ToArray();
        return p.Length switch { 0 => "?", 1 => p[0][..1].ToUpperInvariant(), _ => (p[0][..1] + p[^1][..1]).ToUpperInvariant() };
    }

    public static string Papel(string? papel) => papel switch
    {
        "SuperAdmin" => "Super Admin",
        "Suporte" => "Suporte",
        "AdminEmpresa" => "Administrador",
        "RH" => "RH",
        "Gestor" => "Gestor",
        "Funcionario" => "Funcionário",
        _ => papel ?? ""
    };
}
