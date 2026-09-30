namespace PersonaliPonto.Shared.Contracts;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Suporte = "Suporte";
    public const string AdminEmpresa = "AdminEmpresa";
    public const string RH = "RH";
    public const string Gestor = "Gestor";
    public const string Funcionario = "Funcionario";

    // Canal (revenda): revendedor e parceiro.
    public const string AdminRevendedor = "AdminRevendedor";
    public const string SuporteRevendedor = "SuporteRevendedor";
    public const string AdminParceiro = "AdminParceiro";
    public const string SuporteParceiro = "SuporteParceiro";

    public const string GestaoEmpresa = AdminEmpresa + "," + RH;
    public const string GestaoEquipe = AdminEmpresa + "," + RH + "," + Gestor;
    public const string Plataforma = SuperAdmin + "," + Suporte;
    public const string Canal = AdminRevendedor + "," + SuporteRevendedor + "," + AdminParceiro + "," + SuporteParceiro;

    public static readonly string[] PapeisPlataforma = [SuperAdmin, Suporte];
    public static readonly string[] PapeisRevendedor = [AdminRevendedor, SuporteRevendedor];
    public static readonly string[] PapeisParceiro = [AdminParceiro, SuporteParceiro];
    public static readonly string[] PapeisCanal = [AdminRevendedor, SuporteRevendedor, AdminParceiro, SuporteParceiro];

    public static bool EhPlataforma(string? papel) => papel is SuperAdmin or Suporte;
    public static bool EhCanal(string? papel) => papel is AdminRevendedor or SuporteRevendedor or AdminParceiro or SuporteParceiro;
}

public static class PersonaliPontoClaims
{
    public const string TenantId = "tenant_id";
    public const string CanalId = "canal_id";
    public const string Escopo = "escopo";
    public const string SuporteAcesso = "suporte_acesso";
    public const string FuncionarioId = "funcionario_id";
    public const string UserId = "sub";
}

public sealed record LoginRequest(string Email, string Senha, string? CodigoMfa = null);

public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiraEm, string RefreshToken, string Nome, string Papel);

public sealed record RefreshRequest(string RefreshToken);
