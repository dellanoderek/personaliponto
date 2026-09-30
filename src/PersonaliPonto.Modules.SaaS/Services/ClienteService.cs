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
    int DiasTeste = 15,
    Guid? CanalDonoId = null,
    TipoEntidade TipoEntidade = TipoEntidade.Privada,
    Guid? MunicipioId = null);

public sealed record ClienteCriado(Guid TenantId, string? EmailAdmin, string? SenhaTemporaria);

/// <summary>Gestão de clientes pelo Super Admin: cadastrar, ativar, suspender, cancelar e reativar.</summary>
public sealed class ClienteService(ISaasDbContext db, RegistroRepService registros, AuditService audit, IClock clock,
    ITenantContext contexto, IUnicidadeGlobal unicidade, ICurrentUser usuario)
{
    /// <summary>No painel de canal, apenas o administrador (revendedor/parceiro) altera cadastro e contrato.</summary>
    private void ExigirGestor()
    {
        if (!contexto.IsSystem && contexto.CanalId is not null && usuario.Papel is not (Roles.AdminRevendedor or Roles.AdminParceiro))
            throw new AcessoNegadoException("Apenas o administrador do canal pode alterar clientes.");
    }

    /// <summary>
    /// Canal dono padrão: o informado (se visível), o canal do usuário ou, para a plataforma, o Owner raiz.
    /// O filtro global de canais garante que um canal fora da hierarquia do usuário não seja encontrado.
    /// </summary>
    public async Task<Canal> CanalDonoAsync(Guid? canalDonoId, CancellationToken ct)
    {
        var id = canalDonoId ?? contexto.CanalId ?? (contexto.IsSystem ? Canal.OwnerRaizId : throw new AcessoNegadoException("Operação exige um canal."));
        return await db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NaoEncontradoException("Canal dono não encontrado.");
    }

    /// <summary>Fornecedor exibido por padrão: a marca do canal dono (ex.: "PERSONALIPONTO" para o Owner).</summary>
    public static string FornecedorPadrao(Canal canal) => canal.NomeMarca.Trim().ToUpperInvariant();

    /// <summary>Entra no tenant para gravar dados que não são da carteira do canal (empresa, administrador, REP).</summary>
    private IDisposable? EntrarNoCliente(Guid tenantId) => contexto.IsSystem ? null : contexto.ComoTenant(tenantId);

    public async Task<ClienteCriado> CadastrarAsync(NovoClienteRequest r, CancellationToken ct)
    {
        ExigirGestor();
        if (!Documentos.CnpjValido(r.Cnpj) && !Documentos.CpfValido(r.Cnpj)) throw new RegraNegocioException("CNPJ/CPF inválido.");
        if (string.IsNullOrWhiteSpace(r.RazaoSocial)) throw new RegraNegocioException("Razão social obrigatória.");
        var comAdmin = !string.IsNullOrWhiteSpace(r.EmailAdmin);
        if (comAdmin && !r.EmailAdmin.Contains('@')) throw new RegraNegocioException("E-mail do administrador inválido.");
        if (r.DiaVencimento is < 1 or > 28) throw new RegraNegocioException("Dia de vencimento entre 1 e 28.");
        var doc = Documentos.Normalizar(r.Cnpj);
        var email = comAdmin ? r.EmailAdmin.Trim().ToLowerInvariant() : null;
        // Unicidade global (CNPJ ativo e e-mail de login), sem expor dados de outras carteiras.
        if (await unicidade.CnpjClienteAtivoAsync(doc, ct)) throw new RegraNegocioException("Já existe cliente ativo com este CNPJ.");
        if (email is not null && await unicidade.EmailEmUsoAsync(email, ct)) throw new RegraNegocioException("E-mail já utilizado por outro usuário.");
        var canal = await CanalDonoAsync(r.CanalDonoId, ct);
        if (r.MunicipioId is { } mun && !await db.Municipios.AnyAsync(m => m.Id == mun, ct)) throw new NaoEncontradoException("Município não encontrado.");

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
            TelefoneContato = r.Telefone,
            CanalDonoId = canal.Id,
            TipoEntidade = r.TipoEntidade,
            MunicipioId = r.MunicipioId
        };
        db.Tenants.Add(tenant);
        contexto.IncluirTenantCanal(tenant.Id, canal.Id);
        using var _ = EntrarNoCliente(tenant.Id);

        db.Assinaturas.Add(new Assinatura
        {
            TenantId = tenant.Id, PlanoId = r.PlanoId, RazaoSocial = tenant.Nome, Cnpj = doc, Telefone = r.Telefone, Email = email,
            ValorMensal = r.ValorMensal, FuncionariosContratados = r.FuncionariosContratados, CustoMensal = r.CustoMensal,
            Fornecedor = string.IsNullOrWhiteSpace(r.Fornecedor) ? FornecedorPadrao(canal) : r.Fornecedor.Trim().ToUpperInvariant(),
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
        ExigirGestor();
        email = email.Trim().ToLowerInvariant();
        if (!email.Contains('@')) throw new RegraNegocioException("E-mail inválido.");
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) throw new NaoEncontradoException("Cliente não encontrado.");
        var senha = SenhaTemporaria();
        using var _ = EntrarNoCliente(tenantId);
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email && x.TenantId == tenantId, ct);
        if (u is null && await unicidade.EmailEmUsoAsync(email, ct)) throw new RegraNegocioException("E-mail já utilizado por outro cliente.");
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
        ExigirGestor();
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
        ExigirGestor();
        var a = await db.Assinaturas.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct) ?? throw new NaoEncontradoException("Assinatura não encontrada.");
        a.CanceladaEm = null;
        a.MotivoCancelamento = null;
        await MudarStatus(tenantId, TenantStatus.Ativo, "cliente.reativado", null, ct);
    }

    public async Task AtualizarContratoAsync(Guid tenantId, decimal valor, int funcionarios, decimal custo, string? fornecedor, int diaVencimento,
        bool bloqueioAutomatico, int diasTolerancia, int diasParaBloqueio, string? telefone, string? observacoes, CancellationToken ct)
    {
        ExigirGestor();
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
        ExigirGestor();
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
        while (await unicidade.SlugTenantEmUsoAsync(slug, ct)) slug = $"{baseSlug}-{i++}";
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
