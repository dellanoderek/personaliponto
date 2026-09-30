namespace PersonaliPonto.Shared.Contracts;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Suporte = "Suporte";
    public const string AdminEmpresa = "AdminEmpresa";
    public const string RH = "RH";
    public const string Gestor = "Gestor";
    public const string Funcionario = "Funcionario";

    public const string GestaoEmpresa = AdminEmpresa + "," + RH;
    public const string GestaoEquipe = AdminEmpresa + "," + RH + "," + Gestor;
    public const string Plataforma = SuperAdmin + "," + Suporte;
}

public static class PersonaliPontoClaims
{
    public const string TenantId = "tenant_id";
    public const string FuncionarioId = "funcionario_id";
    public const string UserId = "sub";
}

public sealed record LoginRequest(string Email, string Senha, string? CodigoMfa = null);

public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiraEm, string RefreshToken, string Nome, string Papel);

public sealed record RefreshRequest(string RefreshToken);
