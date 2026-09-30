using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Formatacao;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Modules.SaaS.Services;

/// <summary>
/// Verificações de unicidade que precisam enxergar todos os registros (e-mail de login, CNPJ, slugs),
/// sem expor os dados: respondem apenas "em uso"/"livre". Implementada na infraestrutura.
/// </summary>
public interface IUnicidadeGlobal
{
    Task<bool> EmailEmUsoAsync(string email, CancellationToken ct);
    Task<bool> CnpjClienteAtivoAsync(string cnpj, CancellationToken ct);
    Task<bool> SlugTenantEmUsoAsync(string slug, CancellationToken ct);
    Task<bool> SlugCanalEmUsoAsync(string slug, CancellationToken ct);
    Task<bool> CnpjCanalEmUsoAsync(string cnpj, CancellationToken ct);
}

public sealed record NovoCanalRequest(
    string RazaoSocial,
    string Cnpj,
    string NomeMarca,
    string? Email,
    string? Telefone,
    string NomeAdmin,
    string EmailAdmin,
    string? CpfAdmin,
    Guid? CanalPaiId = null);

public sealed record CanalCriado(Guid CanalId, TipoCanal Tipo, string EmailAdmin, string SenhaTemporaria);

public sealed record LinhaCanal(Guid Id, TipoCanal Tipo, string NomeMarca, string RazaoSocial, string Cnpj, StatusCanal Status,
    DateTimeOffset CriadoEm, int Clientes, int ClientesAtivos, int Parceiros, decimal ReceitaMensal);

public sealed record IndicadoresCanal(
    int Clientes, int Ativos, int EmTeste, int Suspensos, int Cancelados, int Parceiros,
    decimal ReceitaMensal, int FuncionariosContratados, decimal Vencido);

/// <summary>
/// Hierarquia de canais (Owner → Revendedor → Parceiro). O Owner cria revendedores; o revendedor cria parceiros;
/// o parceiro não tem filhos. As regras são repetidas no banco (trigger/check) — a aplicação não é a única barreira.
/// </summary>
public sealed class CanalService(ISaasDbContext db, ITenantContext contexto, ICurrentUser usuario, IUnicidadeGlobal unicidade, AuditService audit, IClock clock)
{
    /// <summary>Somente o Super Admin (plataforma) ou o administrador do canal pai criam canais e alteram situações.</summary>
    private void ExigirAdministrador()
    {
        if (contexto.IsSystem ? usuario.Papel is Roles.Suporte : usuario.Papel is not (Roles.AdminRevendedor or Roles.AdminParceiro))
            throw new AcessoNegadoException("Apenas o administrador pode gerenciar canais.");
    }

    /// <summary>Canal "raiz" do contexto: o canal do usuário ou, para a plataforma, o Owner.</summary>
    public Guid CanalBase => contexto.CanalId ?? (contexto.IsSystem ? Canal.OwnerRaizId : throw new AcessoNegadoException("Operação exige um canal."));

    public Task<Canal?> ObterAsync(Guid canalId, CancellationToken ct) =>
        db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == canalId, ct);

    public async Task<CanalCriado> CriarAsync(NovoCanalRequest r, CancellationToken ct)
    {
        ExigirAdministrador();
        var paiId = r.CanalPaiId ?? CanalBase;
        // Filtro global + RLS: canal fora da hierarquia do usuário simplesmente não é encontrado.
        var pai = await db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == paiId, ct) ?? throw new NaoEncontradoException("Canal pai não encontrado.");
        var tipo = Canal.TipoFilho(pai.Tipo)
            ?? throw new RegraNegocioException("Profundidade máxima atingida: parceiros não podem ter canais abaixo deles.");

        var doc = Documentos.Normalizar(r.Cnpj);
        if (!Documentos.CnpjValido(doc) && !Documentos.CpfValido(doc)) throw new RegraNegocioException("CNPJ/CPF inválido.");
        if (string.IsNullOrWhiteSpace(r.RazaoSocial)) throw new RegraNegocioException("Razão social obrigatória.");
        if (string.IsNullOrWhiteSpace(r.NomeMarca)) throw new RegraNegocioException("Informe o nome da marca.");
        var email = (r.EmailAdmin ?? "").Trim().ToLowerInvariant();
        if (!email.Contains('@')) throw new RegraNegocioException("E-mail do administrador inválido.");
        var cpf = Documentos.Normalizar(r.CpfAdmin);
        if (!string.IsNullOrEmpty(cpf) && !Documentos.CpfValido(cpf)) throw new RegraNegocioException("CPF do administrador inválido.");
        if (await unicidade.CnpjCanalEmUsoAsync(doc, ct)) throw new RegraNegocioException("Já existe um canal com este CNPJ.");
        if (await unicidade.EmailEmUsoAsync(email, ct)) throw new RegraNegocioException("E-mail já utilizado por outro usuário.");

        var baseSlug = ClienteService.Slug(r.NomeMarca);
        var slug = baseSlug;
        for (var i = 2; await unicidade.SlugCanalEmUsoAsync(slug, ct); i++) slug = $"{baseSlug}-{i}";

        var agora = clock.UtcNow;
        var canal = new Canal
        {
            Tipo = tipo, CanalPaiId = pai.Id, Nivel = Canal.NivelDe(tipo), Slug = slug,
            RazaoSocial = r.RazaoSocial.Trim(), Cnpj = doc, NomeMarca = r.NomeMarca.Trim(), Email = r.Email?.Trim(),
            Telefone = r.Telefone?.Trim(), CriadoEm = agora
        };
        canal.Caminho = pai.Caminho + canal.Id + "/";
        db.Canais.Add(canal);
        // O novo canal passa a fazer parte da hierarquia visível (necessário para gravar o administrador dele).
        contexto.IncluirCanalDescendente(canal.Id, pai.Id);

        var senha = ClienteService.SenhaTemporaria();
        var admin = new Usuario
        {
            CanalId = canal.Id, Email = email, Nome = string.IsNullOrWhiteSpace(r.NomeAdmin) ? "Administrador" : r.NomeAdmin.Trim(),
            Cpf = string.IsNullOrEmpty(cpf) ? null : cpf, SenhaHash = SecretHasher.Hash(senha),
            Papel = tipo == TipoCanal.Revendedor ? Roles.AdminRevendedor : Roles.AdminParceiro,
            Escopo = EscopoUsuario.Canal, DeveTrocarSenha = true, CriadoEm = agora
        };
        db.Usuarios.Add(admin);
        audit.Registrar(tipo == TipoCanal.Revendedor ? "canal.revendedor_criado" : "canal.parceiro_criado", nameof(Canal), canal.Id,
            new { canal.RazaoSocial, canal.NomeMarca, doc, pai = pai.Id, admin = email });
        await db.SaveChangesAsync(ct);
        return new CanalCriado(canal.Id, tipo, email, senha);
    }

    /// <summary>Canais filhos diretos do canal informado (ou do canal base), com números da carteira.</summary>
    public async Task<IReadOnlyList<LinhaCanal>> ListarFilhosAsync(Guid? canalPaiId, CancellationToken ct)
    {
        var paiId = canalPaiId ?? CanalBase;
        var filhos = await db.Canais.AsNoTracking().Where(c => c.CanalPaiId == paiId).OrderBy(c => c.NomeMarca).ToListAsync(ct);
        if (filhos.Count == 0) return [];
        var caminhos = filhos.ToDictionary(f => f.Id, f => f.Caminho);
        var todos = await db.Canais.AsNoTracking().Select(c => new { c.Id, c.Caminho, c.CanalPaiId }).ToListAsync(ct);
        var tenants = await db.Tenants.AsNoTracking().Select(t => new { t.Id, t.CanalDonoId, t.Status }).ToListAsync(ct);
        var receitas = await db.Assinaturas.AsNoTracking().Where(a => a.CanceladaEm == null)
            .Select(a => new { a.TenantId, a.ValorMensal }).ToListAsync(ct);
        var receitaPorTenant = receitas.GroupBy(x => x.TenantId).ToDictionary(g => g.Key, g => g.Sum(x => x.ValorMensal));

        return filhos.Select(f =>
        {
            var sub = todos.Where(c => c.Caminho.StartsWith(f.Caminho, StringComparison.Ordinal)).Select(c => c.Id).ToHashSet();
            var ts = tenants.Where(t => sub.Contains(t.CanalDonoId)).ToList();
            return new LinhaCanal(f.Id, f.Tipo, f.NomeMarca, f.RazaoSocial, f.Cnpj, f.Status, f.CriadoEm, ts.Count,
                ts.Count(t => t.Status == TenantStatus.Ativo), todos.Count(c => c.CanalPaiId == f.Id),
                ts.Sum(t => receitaPorTenant.GetValueOrDefault(t.Id)));
        }).ToList();
    }

    /// <summary>Indicadores comerciais da carteira visível (própria + descendentes). Não lê dados de ponto.</summary>
    public async Task<IndicadoresCanal> IndicadoresAsync(DateOnly hoje, CancellationToken ct)
    {
        var tenants = await db.Tenants.AsNoTracking().Select(t => new { t.Id, t.Status }).ToListAsync(ct);
        var vigentes = await db.Assinaturas.AsNoTracking().Where(a => a.CanceladaEm == null).ToListAsync(ct);
        var vencido = await db.Faturas.AsNoTracking().Where(f => f.Status == StatusFatura.Aberta && f.Vencimento < hoje)
            .SumAsync(f => (decimal?)f.Valor, ct) ?? 0;
        var baseId = CanalBase;
        var parceiros = await db.Canais.AsNoTracking().CountAsync(c => c.CanalPaiId == baseId, ct);
        return new IndicadoresCanal(tenants.Count, tenants.Count(t => t.Status == TenantStatus.Ativo), tenants.Count(t => t.Status == TenantStatus.Teste),
            tenants.Count(t => t.Status is TenantStatus.Suspenso or TenantStatus.Inadimplente), tenants.Count(t => t.Status == TenantStatus.Cancelado),
            parceiros, vigentes.Sum(a => a.ValorMensal), vigentes.Sum(a => a.FuncionariosContratados), vencido);
    }

    /// <summary>Altera a situação comercial de um canal descendente (nunca a do próprio canal).</summary>
    public async Task AlterarStatusAsync(Guid canalId, StatusCanal status, CancellationToken ct)
    {
        ExigirAdministrador();
        if (canalId == contexto.CanalId) throw new AcessoNegadoException("Um canal não pode alterar a própria situação.");
        var c = await db.Canais.FirstOrDefaultAsync(x => x.Id == canalId, ct) ?? throw new NaoEncontradoException("Canal não encontrado.");
        if (c.Tipo == TipoCanal.Owner) throw new RegraNegocioException("O Owner não tem situação comercial.");
        var anterior = c.Status;
        c.Status = status;
        audit.Registrar("canal.status_alterado", nameof(Canal), c.Id, new { anterior, status });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Canais que podem ser dono de um cliente cadastrado pelo usuário (o próprio e descendentes).</summary>
    public Task<List<Canal>> CanaisDaHierarquiaAsync(CancellationToken ct) =>
        db.Canais.AsNoTracking().OrderBy(c => c.Nivel).ThenBy(c => c.NomeMarca).ToListAsync(ct);
}
