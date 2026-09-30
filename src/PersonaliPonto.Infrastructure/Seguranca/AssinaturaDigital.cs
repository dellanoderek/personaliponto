using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using PersonaliPonto.Core.RepP.Abstractions;

namespace PersonaliPonto.Infrastructure.Seguranca;

public sealed class AssinaturaOptions
{
    /// <summary>Certificado A1 (PFX) do desenvolvedor, em base64 (preferencial em produção via variável de ambiente).</summary>
    public string? CertificadoBase64 { get; set; }
    public string? CertificadoArquivo { get; set; }
    public string? CertificadoSenha { get; set; }
}

/// <summary>
/// Assinaturas exigidas pela Portaria 671 (art. 86 a 88): CAdES destacado (.p7s) para AFD/AEJ e PAdES para o
/// comprovante em PDF. Em produção exige certificado ICP-Brasil; sem certificado configurado, usa um
/// certificado autoassinado de desenvolvimento e sinaliza <see cref="CertificadoIcpBrasil"/> = false.
/// </summary>
public sealed class AssinaturaDigital : IAssinaturaDigital, IDisposable
{
    private readonly X509Certificate2 _cert;

    public AssinaturaDigital(IOptions<AssinaturaOptions> options, ILogger<AssinaturaDigital> log)
    {
        var o = options.Value;
        if (!string.IsNullOrWhiteSpace(o.CertificadoBase64))
            _cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(o.CertificadoBase64), o.CertificadoSenha, X509KeyStorageFlags.EphemeralKeySet);
        else if (!string.IsNullOrWhiteSpace(o.CertificadoArquivo) && File.Exists(o.CertificadoArquivo))
            _cert = X509CertificateLoader.LoadPkcs12FromFile(o.CertificadoArquivo, o.CertificadoSenha, X509KeyStorageFlags.EphemeralKeySet);
        else
        {
            _cert = CertificadoDesenvolvimento();
            log.LogWarning("Assinatura digital usando certificado AUTOASSINADO de desenvolvimento. Configure um certificado ICP-Brasil (e-CNPJ A1) antes de produção.");
        }

        if (!_cert.HasPrivateKey) throw new InvalidOperationException("Certificado de assinatura sem chave privada.");
        CertificadoIcpBrasil = EmitidoPorIcpBrasil(_cert);
        TitularCertificado = _cert.GetNameInfo(X509NameType.SimpleName, false);
    }

    public bool CertificadoIcpBrasil { get; }
    public string? TitularCertificado { get; }
    public DateTimeOffset ValidoAte => _cert.NotAfter;

    public byte[] AssinarCadesDestacado(byte[] conteudo)
    {
        var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _cert)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            IncludeOption = X509IncludeOption.WholeChain
        };
        signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
        signer.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), SigningCertificateV2(_cert)));
        cms.ComputeSignature(signer, silent: true);
        return cms.Encode();
    }

    public byte[] AssinarPdf(byte[] pdf, string motivo)
    {
        using var input = new MemoryStream(pdf);
        var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var options = new DigitalSignatureOptions
        {
            Reason = motivo,
            Location = "Brasil",
            ContactInfo = TitularCertificado ?? "",
            AppName = "PersonaliPonto REP-P",
            PageIndex = 0,
            Rectangle = new PdfSharp.Drawing.XRect(0, 0, 0, 0)
        };
        var signer = new PdfSharpDefaultSigner(_cert, PdfMessageDigestType.SHA256, null);
        DigitalSignatureHandler.ForDocument(doc, signer, options);
        using var output = new MemoryStream();
        doc.Save(output);
        return output.ToArray();
    }

    /// <summary>Verifica uma assinatura CAdES destacada (usado em testes e na conferência de arquivos).</summary>
    public static bool VerificarCades(byte[] conteudo, byte[] p7s)
    {
        try
        {
            var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
            cms.Decode(p7s);
            cms.CheckSignature(verifySignatureOnly: true);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Atributo ESS signing-certificate-v2 (RFC 5035), exigido pelo perfil CAdES-BES.</summary>
    private static byte[] SigningCertificateV2(X509Certificate2 cert)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())                     // SigningCertificateV2
        using (w.PushSequence())                     // certs: SEQUENCE OF ESSCertIDv2
        using (w.PushSequence())                     // ESSCertIDv2 (hashAlgorithm padrão = SHA-256, omitido)
        {
            w.WriteOctetString(SHA256.HashData(cert.RawData));
            using (w.PushSequence())                 // IssuerSerial
            {
                using (w.PushSequence())             // GeneralNames
                using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, true))) // directoryName [4]
                    w.WriteEncodedValue(cert.IssuerName.RawData);
                w.WriteInteger(new System.Numerics.BigInteger(cert.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true));
            }
        }
        return w.Encode();
    }

    private static bool EmitidoPorIcpBrasil(X509Certificate2 cert)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.Build(cert);
        // Certificados ICP-Brasil têm "O=ICP-Brasil" nos emissores da cadeia (AC Raiz Brasileira → AC intermediária).
        static bool Icp(string dn) => dn.Split(',').Select(p => p.Trim()).Any(p => p.Equals("O=ICP-Brasil", StringComparison.OrdinalIgnoreCase));
        return !cert.Subject.Equals(cert.Issuer, StringComparison.Ordinal)
               && (Icp(cert.Issuer) || chain.ChainElements.Skip(1).Any(e => Icp(e.Certificate.Subject)));
    }

    private static X509Certificate2 CertificadoDesenvolvimento()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=PersonaliPonto - certificado de DESENVOLVIMENTO, O=PersonaliPonto", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.EphemeralKeySet);
    }

    public void Dispose() => _cert.Dispose();
}
