using Microsoft.EntityFrameworkCore;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;

using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Services;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Seguranca;

public sealed record AcessoCriado(Guid UsuarioId, string Login, string SenhaTemporaria);

/// <summary>Gestão de usuários do tenant (RH, gestores e acesso do funcionário ao app).</summary>
public sealed class UsuarioService(PersonaliPontoDbContext db, RequestContext ctx, AuditService audit, IClock clock)
{
    public const string DominioInterno = "usuarios.personaliponto.local";

    private static readonly string[] PapeisDoTenant = [Roles.AdminEmpresa, Roles.RH, Roles.Gestor, Roles.Funcionario];

    public async Task<AcessoCriado> CriarAsync(string nome, string? email, string? cpf, string papel, Guid? funcionarioId, CancellationToken ct)
    {
        var tenantId = ctx.TenantId ?? throw new RegraNegocioException("Operação exige um tenant.");
        if (!PapeisDoTenant.Contains(papel)) throw new RegraNegocioException("Papel inválido.");
        if (papel == Roles.AdminEmpresa && ctx.Papel != Roles.AdminEmpresa) throw new AcessoNegadoException("Apenas o administrador pode criar outro administrador.");
        var cpfN = PersonaliPonto.Core.RepP.Formatacao.Documentos.SomenteDigitos(cpf);
        if (cpfN.Length > 0 && !PersonaliPonto.Core.RepP.Formatacao.Documentos.CpfValido(cpfN)) throw new RegraNegocioException("CPF inválido.");
        var login = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (login is null && cpfN.Length == 0) throw new RegraNegocioException("Informe e-mail ou CPF.");
        if (login is not null && !login.Contains('@')) throw new RegraNegocioException("E-mail inválido.");

        string emailFinal;
        using (ctx.ComoSistema())
        {
            emailFinal = login ?? $"{cpfN}.{tenantId:N}@{DominioInterno}";
            if (await db.Usuarios.AnyAsync(u => u.Email == emailFinal, ct)) throw new RegraNegocioException("Já existe usuário com este e-mail.");
            if (funcionarioId is not null && await db.Usuarios.AnyAsync(u => u.FuncionarioId == funcionarioId && u.Ativo, ct))
                throw new RegraNegocioException("Este funcionário já possui acesso.");
        }

        var senha = ClienteService.SenhaTemporaria();
        var u = new Usuario
        {
            TenantId = tenantId, Nome = nome.Trim(), Email = emailFinal, Cpf = cpfN.Length == 0 ? null : cpfN, Papel = papel,
            FuncionarioId = funcionarioId, SenhaHash = SecretHasher.Hash(senha), DeveTrocarSenha = true, CriadoEm = clock.UtcNow
        };
        db.Usuarios.Add(u);
        audit.Registrar("usuario.criado", nameof(Usuario), u.Id, new { u.Nome, papel, funcionarioId });
        await db.SaveChangesAsync(ct);
        return new AcessoCriado(u.Id, login ?? PersonaliPonto.Core.RepP.Formatacao.Documentos.FormatarCpf(cpfN), senha);
    }

    /// <summary>Cria o acesso do funcionário ao app (login por CPF ou e-mail).</summary>
    public async Task<AcessoCriado> CriarAcessoFuncionarioAsync(Guid funcionarioId, CancellationToken ct)
    {
        var f = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == funcionarioId, ct) ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        return await CriarAsync(f.Nome, f.Email, f.Cpf, Roles.Funcionario, f.Id, ct);
    }

    public async Task<string> RedefinirSenhaAsync(Guid usuarioId, CancellationToken ct)
    {
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId, ct) ?? throw new NaoEncontradoException("Usuário não encontrado.");
        var senha = ClienteService.SenhaTemporaria();
        u.SenhaHash = SecretHasher.Hash(senha);
        u.DeveTrocarSenha = true;
        u.TentativasFalhas = 0;
        u.BloqueadoAte = null;
        audit.Registrar("usuario.senha_redefinida", nameof(Usuario), u.Id);
        await db.SaveChangesAsync(ct);
        return senha;
    }

    public async Task AlterarAsync(Guid usuarioId, string papel, bool ativo, CancellationToken ct)
    {
        if (!PapeisDoTenant.Contains(papel)) throw new RegraNegocioException("Papel inválido.");
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId, ct) ?? throw new NaoEncontradoException("Usuário não encontrado.");
        if (u.Id == ctx.UserId && !ativo) throw new RegraNegocioException("Você não pode desativar o próprio usuário.");
        u.Papel = papel;
        u.Ativo = ativo;
        audit.Registrar("usuario.alterado", nameof(Usuario), u.Id, new { papel, ativo });
        await db.SaveChangesAsync(ct);
    }

    public Task<List<Usuario>> ListarAsync(CancellationToken ct) =>
        db.Usuarios.AsNoTracking().OrderBy(u => u.Nome).ToListAsync(ct);

    // ---------------- Usuários de canal (revendedor/parceiro) ----------------

    /// <summary>Papéis que um canal pode atribuir aos próprios usuários, conforme o tipo do canal.</summary>
    public static string[] PapeisDoCanal(Modules.SaaS.Domain.TipoCanal tipo) => tipo switch
    {
        Modules.SaaS.Domain.TipoCanal.Revendedor => Roles.PapeisRevendedor,
        Modules.SaaS.Domain.TipoCanal.Parceiro => Roles.PapeisParceiro,
        _ => []
    };

    private async Task<Modules.SaaS.Domain.Canal> CanalDoAdminAsync(CancellationToken ct)
    {
        var canalId = ctx.CanalId ?? throw new AcessoNegadoException("Operação exige um canal.");
        if (ctx.Papel is not (Roles.AdminRevendedor or Roles.AdminParceiro))
            throw new AcessoNegadoException("Apenas o administrador do canal gerencia usuários.");
        return await db.Canais.AsNoTracking().FirstOrDefaultAsync(c => c.Id == canalId, ct) ?? throw new NaoEncontradoException("Canal não encontrado.");
    }

    public Task<List<Usuario>> ListarUsuariosCanalAsync(CancellationToken ct)
    {
        var canalId = ctx.CanalId ?? throw new AcessoNegadoException("Operação exige um canal.");
        return db.Usuarios.AsNoTracking().Where(u => u.CanalId == canalId).OrderBy(u => u.Nome).ToListAsync(ct);
    }

    public async Task<AcessoCriado> CriarUsuarioCanalAsync(string nome, string email, string papel, CancellationToken ct)
    {
        var canal = await CanalDoAdminAsync(ct);
        if (!PapeisDoCanal(canal.Tipo).Contains(papel)) throw new RegraNegocioException("Papel inválido para este canal.");
        var login = (email ?? "").Trim().ToLowerInvariant();
        if (!login.Contains('@')) throw new RegraNegocioException("E-mail inválido.");
        if (string.IsNullOrWhiteSpace(nome)) throw new RegraNegocioException("Informe o nome.");
        using (ctx.ComoSistema())
            if (await db.Usuarios.AnyAsync(u => u.Email == login, ct)) throw new RegraNegocioException("Já existe usuário com este e-mail.");

        var senha = ClienteService.SenhaTemporaria();
        var u = new Usuario
        {
            CanalId = canal.Id, Escopo = EscopoUsuario.Canal, Nome = nome.Trim(), Email = login, Papel = papel,
            SenhaHash = SecretHasher.Hash(senha), DeveTrocarSenha = true, CriadoEm = clock.UtcNow
        };
        db.Usuarios.Add(u);
        audit.Registrar("canal.usuario_criado", nameof(Usuario), u.Id, new { u.Nome, papel });
        await db.SaveChangesAsync(ct);
        return new AcessoCriado(u.Id, login, senha);
    }

    public async Task AlterarUsuarioCanalAsync(Guid usuarioId, string papel, bool ativo, CancellationToken ct)
    {
        var canal = await CanalDoAdminAsync(ct);
        if (!PapeisDoCanal(canal.Tipo).Contains(papel)) throw new RegraNegocioException("Papel inválido para este canal.");
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.CanalId == canal.Id, ct) ?? throw new NaoEncontradoException("Usuário não encontrado.");
        if (u.Id == ctx.UserId && (!ativo || papel != u.Papel)) throw new RegraNegocioException("Você não pode rebaixar ou desativar o próprio usuário.");
        u.Papel = papel;
        u.Ativo = ativo;
        audit.Registrar("canal.usuario_alterado", nameof(Usuario), u.Id, new { papel, ativo });
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> RedefinirSenhaCanalAsync(Guid usuarioId, CancellationToken ct)
    {
        var canal = await CanalDoAdminAsync(ct);
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.CanalId == canal.Id, ct) ?? throw new NaoEncontradoException("Usuário não encontrado.");
        var senha = ClienteService.SenhaTemporaria();
        u.SenhaHash = SecretHasher.Hash(senha);
        u.DeveTrocarSenha = true;
        u.TentativasFalhas = 0;
        u.BloqueadoAte = null;
        audit.Registrar("canal.usuario_senha_redefinida", nameof(Usuario), u.Id);
        await db.SaveChangesAsync(ct);
        return senha;
    }
}

public sealed record TerminalAtivado(Guid TerminalId, string Token);

/// <summary>Terminais Web de marcação: o navegador é ativado uma vez pelo RH e passa a identificar o estabelecimento.</summary>
public sealed class TerminalService(PersonaliPontoDbContext db, RequestContext ctx, AuditService audit, IClock clock)
{
    public async Task<TerminalAtivado> AtivarAsync(Guid estabelecimentoId, string nome, CancellationToken ct)
    {
        var tenantId = ctx.TenantId ?? throw new RegraNegocioException("Operação exige um tenant.");
        if (!await db.Estabelecimentos.AnyAsync(e => e.Id == estabelecimentoId, ct)) throw new NaoEncontradoException("Estabelecimento não encontrado.");
        var token = SecretHasher.NovoToken(32);
        var t = new TerminalWeb
        {
            TenantId = tenantId, EstabelecimentoId = estabelecimentoId, Nome = string.IsNullOrWhiteSpace(nome) ? "Terminal" : nome.Trim(),
            TokenHash = SecretHasher.HashToken(token), CriadoEm = clock.UtcNow
        };
        db.TerminaisWeb.Add(t);
        audit.Registrar("terminal.ativado", nameof(TerminalWeb), t.Id, new { t.Nome, estabelecimentoId });
        await db.SaveChangesAsync(ct);
        return new TerminalAtivado(t.Id, token);
    }

    public async Task DesativarAsync(Guid terminalId, CancellationToken ct)
    {
        var t = await db.TerminaisWeb.FirstOrDefaultAsync(x => x.Id == terminalId, ct) ?? throw new NaoEncontradoException("Terminal não encontrado.");
        t.Ativo = false;
        audit.Registrar("terminal.desativado", nameof(TerminalWeb), t.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resolve o terminal pelo token (busca de sistema) e fixa o tenant do contexto.</summary>
    public async Task<TerminalWeb?> ResolverAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = SecretHasher.HashToken(token);
        TerminalWeb? t;
        using (ctx.ComoSistema())
            t = await db.TerminaisWeb.AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash && x.Ativo, ct);
        if (t is null) return null;
        ctx.DefinirTenant(t.TenantId);
        await db.TerminaisWeb.Where(x => x.Id == t.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimoUso, clock.UtcNow), ct);
        return t;
    }
}
