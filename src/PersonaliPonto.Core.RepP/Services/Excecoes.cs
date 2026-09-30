namespace PersonaliPonto.Core.RepP.Services;

/// <summary>Violação de regra de negócio (HTTP 422 / mensagem ao usuário).</summary>
public class RegraNegocioException(string mensagem) : Exception(mensagem);

/// <summary>Conflito de sincronização off-line (estado CONFLICT no coletor).</summary>
public sealed class ConflitoMarcacaoException(string mensagem) : RegraNegocioException(mensagem);

/// <summary>Credencial inválida ou bloqueada (HTTP 401/423).</summary>
public sealed class AcessoNegadoException(string mensagem, bool bloqueado = false) : Exception(mensagem)
{
    public bool Bloqueado { get; } = bloqueado;
}

public sealed class NaoEncontradoException(string mensagem) : Exception(mensagem);
