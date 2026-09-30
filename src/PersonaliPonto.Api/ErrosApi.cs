using Microsoft.AspNetCore.Diagnostics;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Seguranca;

namespace PersonaliPonto.Api;

/// <summary>Converte exceções de domínio em ProblemDetails com status HTTP adequado; nunca expõe detalhes internos.</summary>
public static class ErrosApi
{
    public static async Task TratarAsync(HttpContext ctx)
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, titulo, codigo) = ex switch
        {
            ConflitoMarcacaoException => (StatusCodes.Status409Conflict, ex.Message, "conflito"),
            RegraNegocioException => (StatusCodes.Status422UnprocessableEntity, ex.Message, "regra"),
            MfaNecessarioException => (StatusCodes.Status401Unauthorized, ex.Message, "mfa"),
            AcessoNegadoException a => (a.Bloqueado ? StatusCodes.Status423Locked : StatusCodes.Status401Unauthorized, ex.Message, "acesso"),
            NaoEncontradoException => (StatusCodes.Status404NotFound, ex.Message, "nao_encontrado"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Acesso negado.", "proibido"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Requisição inválida.", "requisicao"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno. Tente novamente em instantes.", "interno")
        };
        if (status == StatusCodes.Status500InternalServerError)
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Erros").LogError(ex, "Erro não tratado em {Path}", ctx.Request.Path);

        ctx.Response.StatusCode = status;
        await Results.Problem(title: titulo, statusCode: status, extensions: new Dictionary<string, object?> { ["codigo"] = codigo }).ExecuteAsync(ctx);
    }
}
