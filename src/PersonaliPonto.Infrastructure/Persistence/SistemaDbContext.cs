using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PersonaliPonto.Infrastructure.Persistence;

public class ConfiguracaoSistema
{
    public string Chave { get; set; } = "";
    public string Valor { get; set; } = "";
    public DateTimeOffset AtualizadoEm { get; set; }
}

/// <summary>
/// Dados internos da plataforma (chaves de Data Protection e segredos do servidor), em schema separado,
/// sem acesso pelo papel da aplicação multi-tenant.
/// </summary>
public class SistemaDbContext(DbContextOptions<SistemaDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "personaliponto_sistema";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<ConfiguracaoSistema> Configuracoes => Set<ConfiguracaoSistema>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<DataProtectionKey>(e =>
        {
            e.ToTable("data_protection_keys");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FriendlyName).HasColumnName("friendly_name");
            e.Property(x => x.Xml).HasColumnName("xml");
        });
        b.Entity<ConfiguracaoSistema>(e =>
        {
            e.ToTable("configuracoes");
            e.HasKey(x => x.Chave);
            e.Property(x => x.Chave).HasColumnName("chave").HasMaxLength(100);
            e.Property(x => x.Valor).HasColumnName("valor");
            e.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
        });
    }
}

public sealed class SistemaDesignTimeFactory : IDesignTimeDbContextFactory<SistemaDbContext>
{
    public SistemaDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("PERSONALIPONTO_DB") ?? "Host=localhost;Port=54329;Database=personaliponto;Username=postgres;Password=devpass";
        return new SistemaDbContext(new DbContextOptionsBuilder<SistemaDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__EFMigrationsHistory", SistemaDbContext.Schema)).Options);
    }
}

/// <summary>
/// Par de chaves RSA do terminal Web. A chave pública vai ao navegador para cifrar (RSA-OAEP) o PIN de
/// marcações feitas sem conexão; a privada fica no banco protegida pelo Data Protection.
/// </summary>
public sealed class ChaveTerminal(IDbContextFactory<SistemaDbContext> fabrica, IDataProtectionProvider dp)
{
    private const string Nome = "terminal.rsa";
    private RSA? _rsa;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private async Task<RSA> RsaAsync()
    {
        if (_rsa is not null) return _rsa;
        await _lock.WaitAsync();
        try
        {
            if (_rsa is not null) return _rsa;
            var protetor = dp.CreateProtector("PersonaliPonto.ChaveTerminal");
            await using var db = await fabrica.CreateDbContextAsync();
            var cfg = await db.Configuracoes.FirstOrDefaultAsync(c => c.Chave == Nome);
            var rsa = RSA.Create(2048);
            if (cfg is null)
            {
                db.Configuracoes.Add(new ConfiguracaoSistema { Chave = Nome, Valor = protetor.Protect(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())), AtualizadoEm = DateTimeOffset.UtcNow });
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    // Outra instância criou ao mesmo tempo: usa a dela.
                    db.ChangeTracker.Clear();
                    cfg = await db.Configuracoes.FirstAsync(c => c.Chave == Nome);
                }
            }
            if (cfg is not null) rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(protetor.Unprotect(cfg.Valor)), out _);
            return _rsa = rsa;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Chave pública em SPKI base64 (importável via WebCrypto "spki").</summary>
    public async Task<string> PublicaAsync() => Convert.ToBase64String((await RsaAsync()).ExportSubjectPublicKeyInfo());

    public async Task<string?> DecifrarAsync(string base64)
    {
        try
        {
            var bytes = (await RsaAsync()).Decrypt(Convert.FromBase64String(base64), RSAEncryptionPadding.OaepSHA256);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
