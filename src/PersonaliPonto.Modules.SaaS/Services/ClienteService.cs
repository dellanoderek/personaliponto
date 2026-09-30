using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Modules.SaaS.Services;

public sealed record NovoClienteRequest(
    string RazaoSocial,
    string Cnpj,
    string? Telefone,
    string EmailAdmin,
    string NomeAdmin,
    string? CpfAdmin,
    decimal ValorMensal,
    int FuncionariosContratados,
    decimal CustoMensal,
    string? Fornecedor,
    int DiaVencimento,
    Guid? PlanoId,
    string LocalPrestacao,
    bool EmTeste,
    int DiasTeste = 15);

public sealed record ClienteCriado(Guid TenantId, string? EmailAdmin, string? SenhaTemporaria);

/// <summary>Gestão de clientes pelo Super Admin: cadastrar, ativar, suspender, cancelar e reativar.</summary>
public sealed class ClienteService(ISaasDbContext db, RegistroRepService registros, AuditService audit, IClock clock)
{
    public async Task<ClienteCriado> CadastrarAsync(NovoClienteRequest r, CancellationToken ct)
    {
        if (!Documentos.CnpjValido(r.Cnpj) && !Documentos.CpfValido(r.Cnpj)) throw new RegraNegocioException("CNPJ/CPF inválido.");
        if (string.IsNullOrWhiteSpace(r.RazaoSocial)) throw new RegraNegocioException("Razão social obrigatória.");
        var comAdmin = !string.IsNullOrWhiteSpace(r.EmailAdmin);
        if (comAdmin && !r.EmailAdmin.Contains('@')) throw new RegraNegocioException("E-mail do administrador inválido.");
        if (r.DiaVencimento is < 1 or > 28) throw new RegraNegocioException("Dia de vencimento entre 1 e 28.");
        var doc = Documentos.Normalizar(r.Cnpj);
        var email = comAdmin ? r.EmailAdmin.Trim().ToLowerInvariant() : null;
        if (await db.Assinaturas.AnyAsync(a => a.Cnpj == doc && a.CanceladaEm == null, ct)) throw new RegraNegocioException("Já existe cliente ativo com este CNPJ.");
        if (email is not null && await db.Usuarios.AnyAsync(u => u.Email == email, ct)) throw new RegraNegocioException("E-mail já utilizado por outro usuário.");

        var agora = clock.UtcNow;
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            Nome = r.RazaoSocial.Trim(),
            Slug = await SlugUnicoAsync(r.RazaoSocial, ct),
            Status = r.EmTeste ? TenantStatus.Teste : TenantStatus.Ativo,
            CriadoEm = agora,
            TesteAte = r.EmTeste ? agora.AddDays(r.DiasTeste) : null,
            EmailContato = email,
            TelefoneContato = r.Telefone
        };
        db.Tenants.Add(tenant);

        db.Assinaturas.Add(new Assinatura
        {
            TenantId = tenant.Id, PlanoId = r.PlanoId, RazaoSocial = tenant.Nome, Cnpj = doc, Telefone = r.Telefone, Email = email,
            ValorMensal = r.ValorMensal, FuncionariosContratados = r.FuncionariosContratados, CustoMensal = r.CustoMensal,
            Fornecedor = string.IsNullOrWhiteSpace(r.Fornecedor) ? "TEMPO CERTO" : r.Fornecedor.Trim().ToUpperInvariant(),
            DiaVencimento = r.DiaVencimento, Inicio = hoje
        });

        var tipo = doc.Length == 14 ? TipoIdentificador.Cnpj : TipoIdentificador.Cpf;
        var emp = new Empregador { TenantId = tenant.Id, RazaoSocial = tenant.Nome, TipoIdentificador = tipo, Identificador = doc };
        var est = new Estabelecimento
        {
            TenantId = tenant.Id, EmpregadorId = emp.Id, Nome = "Matriz", TipoIdentificador = tipo, Identificador = doc,
            LocalPrestacao = string.IsNullOrWhiteSpace(r.LocalPrestacao) ? tenant.Nome : r.LocalPrestacao.Trim()
        };
        db.Empregadores.Add(emp);
        db.Estabelecimentos.Add(est);

        string? senha = null;
        if (email is not null)
        {
            senha = SenhaTemporaria();
            db.Usuarios.Add(new Usuario
            {
                TenantId = tenant.Id, Email = email, Nome = string.IsNullOrWhiteSpace(r.NomeAdmin) ? "Administrador" : r.NomeAdmin.Trim(),
                Cpf = Documentos.Normalizar(r.CpfAdmin), SenhaHash = SecretHasher.Hash(senha), Papel = Roles.AdminEmpresa,
                DeveTrocarSenha = true, CriadoEm = agora
            });
        }
        await db.SaveChangesAsync(ct);
        await registros.RegistrarEmpresaAsync(est, emp, r.CpfAdmin, ct);

        audit.Registrar("cliente.cadastrado", nameof(Tenant), tenant.Id, new { tenant.Nome, doc, r.ValorMensal, r.FuncionariosContratados }, tenant.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ClienteCriado(tenant.Id, email, senha);
    }

    /// <summary>Cria (ou recria) o acesso do administrador da empresa. Devolve a senha temporária uma única vez.</summary>
    public async Task<string> CriarAdministradorAsync(Guid tenantId, string email, string nome, string? cpf, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        if (!email.Contains('@')) throw new RegraNegocioException("E-mail inválido.");
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) throw new NaoEncontradoException("Cliente não encontrado.");
        var senha = SenhaTemporaria();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (u is not null && u.TenantId != tenantId) throw new RegraNegocioException("E-mail já utilizado por outro cliente.");
        if (u is null)
        {
            u = new Usuario { TenantId = tenantId, Email = email, CriadoEm = clock.UtcNow, Papel = Roles.AdminEmpresa };
            db.Usuarios.Add(u);
        }
        u.Nome = nome.Trim();
        u.Cpf = Documentos.Normalizar(cpf);
        u.SenhaHash = SecretHasher.Hash(senha);
        u.DeveTrocarSenha = true;
        u.Ativo = true;
        u.TentativasFalhas = 0;
        u.BloqueadoAte = null;
        audit.Registrar("cliente.admin_criado", nameof(Usuario), u.Id, new { email }, tenantId);
        await db.SaveChangesAsync(ct);
        return senha;
    }

    public Task AtivarAsync(Guid tenantId, CancellationToken ct) => MudarStatus(tenantId, TenantStatus.Ativo, "cliente.ativado", null, ct);
    public Task SuspenderAsync(Guid tenantId, string motivo, CancellationToken ct) => MudarStatus(tenantId, TenantStatus.Suspenso, "cliente.suspenso", motivo, ct);

    public async Task CancelarAsync(Guid tenantId, string motivo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new RegraNegocioException("Informe o motivo do cancelamento.");
        var a = await db.Assinaturas.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct) ?? throw new NaoEncontradoException("Assinatura não encontrada.");
        a.CanceladaEm = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        a.MotivoCancelamento = motivo.Trim();
        var abertasFuturas = await db.Faturas.Where(f => f.TenantId == tenantId && f.Status == StatusFatura.Aberta && f.Competencia > a.CanceladaEm).ToListAsync(ct);
        foreach (var f in abertasFuturas) f.Status = StatusFatura.Cancelada;
        await MudarStatus(tenantId, TenantStatus.Cancelado, "cliente.cancelado", motivo, ct);
    }

    public async Task ReativarAsync(Guid tenantId, CancellationToken ct)
    {
        var a = await db.Assinaturas.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct) ?? throw new NaoEncontradoException("Assinatura não encontrada.");
        a.CanceladaEm = null;
        a.MotivoCancelamento = null;
        await MudarStatus(tenantId, TenantStatus.Ativo, "cliente.reativado", null, ct);
    }

    public async Task AtualizarContratoAsync(Guid tenantId, decimal valor, int funcionarios, decimal custo, string? fornecedor, int diaVencimento,
        bool bloqueioAutomatico, int diasTolerancia, int diasParaBloqueio, string? telefone, string? observacoes, CancellationToken ct)
    {
        if (diaVencimento is < 1 or > 28) throw new RegraNegocioException("Dia de vencimento entre 1 e 28.");
        if (diasParaBloqueio < diasTolerancia) throw new RegraNegocioException("O bloqueio deve ocorrer depois da inadimplência.");
        var a = await db.Assinaturas.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct) ?? throw new NaoEncontradoException("Assinatura não encontrada.");
        a.ValorMensal = valor;
        a.FuncionariosContratados = funcionarios;
        a.CustoMensal = custo;
        a.Fornecedor = fornecedor?.Trim().ToUpperInvariant();
        a.DiaVencimento = diaVencimento;
        a.BloqueioAutomatico = bloqueioAutomatico;
        a.DiasTolerancia = diasTolerancia;
        a.DiasParaBloqueio = diasParaBloqueio;
        a.Telefone = telefone;
        a.Observacoes = observacoes;
        audit.Registrar("cliente.contrato_alterado", nameof(Assinatura), a.Id, new { valor, funcionarios, custo, fornecedor, diaVencimento, bloqueioAutomatico }, tenantId);
        await db.SaveChangesAsync(ct);
    }

    private async Task MudarStatus(Guid tenantId, TenantStatus status, string acao, string? motivo, CancellationToken ct)
    {
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        var anterior = t.Status;
        t.Status = status;
        audit.Registrar(acao, nameof(Tenant), t.Id, new { anterior, status, motivo }, tenantId);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> SlugUnicoAsync(string nome, CancellationToken ct)
    {
        var baseSlug = Slug(nome);
        var slug = baseSlug;
        var i = 2;
        while (await db.Tenants.AnyAsync(t => t.Slug == slug, ct)) slug = $"{baseSlug}-{i++}";
        return slug;
    }

    public static string Slug(string texto)
    {
        var norm = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in norm)
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if ((c == ' ' || c == '-') && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var s = sb.ToString().Trim('-');
        return s.Length > 40 ? s[..40].TrimEnd('-') : (s.Length == 0 ? "cliente" : s);
    }

    public static string SenhaTemporaria()
    {
        const string letras = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz";
        const string digitos = "23456789";
        var rnd = System.Security.Cryptography.RandomNumberGenerator.GetItems<char>(letras, 8);
        var num = System.Security.Cryptography.RandomNumberGenerator.GetItems<char>(digitos, 4);
        return new string(rnd) + "-" + new string(num);
    }
}
