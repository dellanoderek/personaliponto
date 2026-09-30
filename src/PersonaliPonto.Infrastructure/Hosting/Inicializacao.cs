using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Seguranca;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Modules.SaaS.Domain;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Hosting;

/// <summary>Aplicação de migrations e dados iniciais da plataforma (Super Admin e planos).</summary>
public static class Inicializacao
{
    public static async Task MigrarAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().DefinirMigracao();
        var db = scope.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        await db.Database.MigrateAsync(ct);
        var fabrica = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SistemaDbContext>>();
        await using var sis = await fabrica.CreateDbContextAsync(ct);
        await sis.Database.MigrateAsync(ct);
    }

    /// <summary>Cria o Super Admin inicial (config "SuperAdmin:Email"/"SuperAdmin:Senha") se não existir nenhum.</summary>
    public static async Task SemearAsync(IServiceProvider sp, IConfiguration cfg, ILogger log, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().DefinirSistema();
        var db = scope.ServiceProvider.GetRequiredService<PersonaliPontoDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // Canal Owner raiz (também criado pela migration): idempotente.
        if (!await db.Canais.AnyAsync(c => c.Id == Canal.OwnerRaizId, ct))
            await db.Database.ExecuteSqlRawAsync(SegurancaBanco.SemearOwner, ct);

        if (!await db.Usuarios.AnyAsync(u => u.Papel == Roles.SuperAdmin, ct))
        {
            var email = cfg["SuperAdmin:Email"];
            var senha = cfg["SuperAdmin:Senha"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
                log.LogWarning("Nenhum Super Admin cadastrado. Defina SuperAdmin:Email e SuperAdmin:Senha para criá-lo na inicialização.");
            else
            {
                AuthService.ValidarForcaSenha(senha);
                db.Usuarios.Add(new Usuario
                {
                    Email = email.Trim().ToLowerInvariant(), Nome = cfg["SuperAdmin:Nome"] ?? "Administrador da plataforma",
                    SenhaHash = SecretHasher.Hash(senha), Papel = Roles.SuperAdmin, DeveTrocarSenha = true, CriadoEm = clock.UtcNow
                });
                log.LogInformation("Super Admin {Email} criado.", email);
            }
        }

        if (!await db.Planos.AnyAsync(ct))
        {
            db.Planos.AddRange(
                new Plano { Nome = "Essencial (até 10)", PrecoMensal = 79.90m, LimiteFuncionarios = 10, PrecoFuncionarioExcedente = 5m },
                new Plano { Nome = "Profissional (até 20)", PrecoMensal = 139.90m, LimiteFuncionarios = 20, PrecoFuncionarioExcedente = 4.5m },
                new Plano { Nome = "Empresarial (até 50)", PrecoMensal = 249.90m, LimiteFuncionarios = 50, PrecoFuncionarioExcedente = 4m },
                new Plano { Nome = "Corporativo (sob consulta)", PrecoMensal = 0m, LimiteFuncionarios = 0, PrecoFuncionarioExcedente = 0m });
        }
        await db.SaveChangesAsync(ct);
    }
}
