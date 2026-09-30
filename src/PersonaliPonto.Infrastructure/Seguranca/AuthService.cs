using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PersonaliPonto.Core.RepP.Abstractions;
using PersonaliPonto.Core.RepP.Domain;
using PersonaliPonto.Core.RepP.Services;
using PersonaliPonto.Infrastructure.Persistence;
using PersonaliPonto.Infrastructure.Tenancy;
using PersonaliPonto.Shared.Contracts;

namespace PersonaliPonto.Infrastructure.Seguranca;

public sealed class JwtOptions
{
    public string Emissor { get; set; } = "personaliponto";
    public string Audiencia { get; set; } = "personaliponto";
    /// <summary>Chave HMAC (mínimo 32 bytes). Obrigatória em produção via variável de ambiente.</summary>
    public string Chave { get; set; } = "";
    public int AcessoMinutos { get; set; } = 15;
    public int RefreshDias { get; set; } = 30;
    public int MaximoTentativas { get; set; } = 5;
    public int BloqueioMinutos { get; set; } = 15;
}

public sealed record UsuarioAutenticado(Usuario Usuario, IReadOnlyList<Claim> Claims);

/// <summary>
/// Autenticação: senha PBKDF2, bloqueio por tentativas, MFA TOTP (RFC 6238) quando habilitado,
/// JWT de curta duração + refresh token com rotação e detecção de reuso.
/// </summary>
public sealed class AuthService(PersonaliPontoDbContext db, RequestContext ctx, IClock clock, AuditService audit, IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public async Task<UsuarioAutenticado> ValidarCredenciaisAsync(string email, string senha, string? codigoMfa, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        email = (email ?? "").Trim().ToLowerInvariant();
        Usuario? u;
        var cpf = PersonaliPonto.Core.RepP.Formatacao.Documentos.SomenteDigitos(email);
        if (!email.Contains('@') && cpf.Length == 11)
        {
            // Login por CPF (funcionários): exige que o CPF identifique um único usuário ativo.
            var candidatos = await db.Usuarios.Where(x => x.Cpf == cpf && x.Ativo).Take(2).ToListAsync(ct);
            if (candidatos.Count > 1) throw new AcessoNegadoException("CPF vinculado a mais de uma empresa. Entre com seu e-mail.");
            u = candidatos.FirstOrDefault();
        }
        else u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email, ct);
        var agora = clock.UtcNow;
        if (u is null || !u.Ativo)
        {
            SecretHasher.Verificar(senha ?? "", "pbkdf2-sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="); // tempo constante
            throw new AcessoNegadoException("E-mail ou senha inválidos.");
        }
        if (u.BloqueadoAte is { } ate && ate > agora)
            throw new AcessoNegadoException("Usuário temporariamente bloqueado por excesso de tentativas.", true);

        if (!SecretHasher.Verificar(senha ?? "", u.SenhaHash))
        {
            u.TentativasFalhas++;
            if (u.TentativasFalhas >= _o.MaximoTentativas)
            {
                u.BloqueadoAte = agora.AddMinutes(_o.BloqueioMinutos);
                u.TentativasFalhas = 0;
                audit.Registrar("auth.bloqueado", nameof(Usuario), u.Id, null, u.TenantId);
            }
            await db.SaveChangesAsync(ct);
            throw new AcessoNegadoException("E-mail ou senha inválidos.");
        }

        if (u.MfaHabilitado)
        {
            if (string.IsNullOrWhiteSpace(codigoMfa)) throw new MfaNecessarioException();
            if (!Totp.Validar(u.MfaSegredo!, codigoMfa, agora)) throw new AcessoNegadoException("Código de verificação inválido.");
        }

        u.TentativasFalhas = 0;
        u.BloqueadoAte = null;
        u.UltimoLogin = agora;
        audit.Registrar("auth.login", nameof(Usuario), u.Id, new { ip = ctx.Ip }, u.TenantId);
        await db.SaveChangesAsync(ct);
        return new UsuarioAutenticado(u, Claims(u));
    }

    public static List<Claim> Claims(Usuario u)
    {
        var c = new List<Claim>
        {
            new(PersonaliPontoClaims.UserId, u.Id.ToString()),
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, u.Papel),
            new(PersonaliPontoClaims.Escopo, u.Escopo.ToString())
        };
        if (u.TenantId is { } t) c.Add(new Claim(PersonaliPontoClaims.TenantId, t.ToString()));
        if (u.CanalId is { } canal) c.Add(new Claim(PersonaliPontoClaims.CanalId, canal.ToString()));
        if (u.FuncionarioId is { } f) c.Add(new Claim(PersonaliPontoClaims.FuncionarioId, f.ToString()));
        if (!string.IsNullOrEmpty(u.Cpf)) c.Add(new Claim("cpf", u.Cpf));
        if (u.DeveTrocarSenha) c.Add(new Claim("trocar_senha", "1"));
        return c;
    }

    public async Task<TokenResponse> EmitirTokensAsync(Usuario u, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        var agora = clock.UtcNow;
        var expira = agora.AddMinutes(_o.AcessoMinutos);
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Chave));
        var jwt = new JwtSecurityToken(_o.Emissor, _o.Audiencia, Claims(u), agora.UtcDateTime, expira.UtcDateTime,
            new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));
        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();
        var access = handler.WriteToken(jwt);

        var refresh = SecretHasher.NovoToken(48);
        db.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = u.Id, TenantId = u.TenantId, TokenHash = SecretHasher.HashToken(refresh),
            CriadoEm = agora, ExpiraEm = agora.AddDays(_o.RefreshDias)
        });
        await db.SaveChangesAsync(ct);
        return new TokenResponse(access, expira, refresh, u.Nome, u.Papel);
    }

    /// <summary>Rotaciona o refresh token. Reuso de token já rotacionado revoga toda a cadeia do usuário.</summary>
    public async Task<TokenResponse> RenovarAsync(string refreshToken, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        var hash = SecretHasher.HashToken(refreshToken ?? "");
        var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct) ?? throw new AcessoNegadoException("Sessão inválida.");
        var agora = clock.UtcNow;
        if (rt.RevogadoEm is not null)
        {
            var todos = await db.RefreshTokens.Where(r => r.UsuarioId == rt.UsuarioId && r.RevogadoEm == null).ToListAsync(ct);
            foreach (var r in todos) r.RevogadoEm = agora;
            audit.Registrar("auth.reuso_refresh", nameof(Usuario), rt.UsuarioId, null, rt.TenantId);
            await db.SaveChangesAsync(ct);
            throw new AcessoNegadoException("Sessão encerrada por segurança. Entre novamente.");
        }
        if (rt.ExpiraEm < agora) throw new AcessoNegadoException("Sessão expirada.");
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == rt.UsuarioId && x.Ativo, ct) ?? throw new AcessoNegadoException("Usuário inativo.");
        rt.RevogadoEm = agora;
        var novo = await EmitirTokensAsync(u, ct);
        rt.SubstituidoPor = (await db.RefreshTokens.FirstAsync(r => r.TokenHash == SecretHasher.HashToken(novo.RefreshToken), ct)).Id;
        await db.SaveChangesAsync(ct);
        return novo;
    }

    public async Task SairAsync(string refreshToken, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        var hash = SecretHasher.HashToken(refreshToken ?? "");
        var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (rt is null || rt.RevogadoEm is not null) return;
        rt.RevogadoEm = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task TrocarSenhaAsync(Guid usuarioId, string atual, string nova, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        ValidarForcaSenha(nova);
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId, ct) ?? throw new NaoEncontradoException("Usuário não encontrado.");
        if (!SecretHasher.Verificar(atual, u.SenhaHash)) throw new AcessoNegadoException("Senha atual incorreta.");
        if (SecretHasher.Verificar(nova, u.SenhaHash)) throw new RegraNegocioException("A nova senha deve ser diferente da atual.");
        u.SenhaHash = SecretHasher.Hash(nova);
        u.DeveTrocarSenha = false;
        foreach (var r in await db.RefreshTokens.Where(r => r.UsuarioId == u.Id && r.RevogadoEm == null).ToListAsync(ct)) r.RevogadoEm = clock.UtcNow;
        audit.Registrar("auth.senha_alterada", nameof(Usuario), u.Id, null, u.TenantId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Inicia a configuração de MFA: gera segredo (ainda não habilitado) e retorna a URI otpauth.</summary>
    public async Task<string> IniciarMfaAsync(Guid usuarioId, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        var u = await db.Usuarios.FirstAsync(x => x.Id == usuarioId, ct);
        var segredo = Totp.NovoSegredo();
        u.MfaSegredo = segredo;
        u.MfaHabilitado = false;
        await db.SaveChangesAsync(ct);
        return $"otpauth://totp/Tempo%20Certo:{Uri.EscapeDataString(u.Email)}?secret={segredo}&issuer=Tempo%20Certo&digits=6&period=30";
    }

    public async Task ConfirmarMfaAsync(Guid usuarioId, string codigo, CancellationToken ct)
    {
        using var _ = ctx.ComoSistema();
        var u = await db.Usuarios.FirstAsync(x => x.Id == usuarioId, ct);
        if (u.MfaSegredo is null || !Totp.Validar(u.MfaSegredo, codigo, clock.UtcNow)) throw new RegraNegocioException("Código inválido.");
        u.MfaHabilitado = true;
        audit.Registrar("auth.mfa_habilitado", nameof(Usuario), u.Id, null, u.TenantId);
        await db.SaveChangesAsync(ct);
    }

    public static void ValidarForcaSenha(string senha)
    {
        if (senha.Length < 10 || !senha.Any(char.IsLetter) || !senha.Any(char.IsDigit))
            throw new RegraNegocioException("A senha deve ter ao menos 10 caracteres, com letras e números.");
    }
}

public sealed class MfaNecessarioException() : Exception("Informe o código de verificação do autenticador.");

/// <summary>TOTP (RFC 6238, SHA-1, 6 dígitos, 30s) com tolerância de ±1 passo.</summary>
public static class Totp
{
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NovoSegredo()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decodificar(string s)
    {
        s = s.TrimEnd('=').ToUpperInvariant();
        var saida = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            var v = Base32.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                saida.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return saida.ToArray();
    }

    public static string Codigo(string segredo, long passo)
    {
        var chave = Decodificar(segredo);
        var msg = BitConverter.GetBytes(passo);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var hash = HMACSHA1.HashData(chave, msg);
        var o = hash[^1] & 0x0F;
        var bin = ((hash[o] & 0x7F) << 24) | (hash[o + 1] << 16) | (hash[o + 2] << 8) | hash[o + 3];
        return (bin % 1_000_000).ToString("000000");
    }

    public static bool Validar(string segredo, string codigo, DateTimeOffset agora)
    {
        var passo = agora.ToUnixTimeSeconds() / 30;
        for (var d = -1; d <= 1; d++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Codigo(segredo, passo + d)), Encoding.ASCII.GetBytes(codigo.Trim())))
                return true;
        return false;
    }
}
