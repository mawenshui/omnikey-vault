using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: X.509 certificate management service.
/// Parses X.509 certificates (PEM and PFX), extracts metadata (subject,
/// issuer, expiry, thumbprint), and provides export capabilities.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class CertificateService
{
    /// <summary>Parses a PEM-encoded certificate.</summary>
    public CertificateInfo ParsePem(string pem)
    {
        var cert = X509Certificate2.CreateFromPem(pem);
        return ExtractInfo(cert);
    }

    /// <summary>Parses a PFX/PKCS12 file with optional password.</summary>
    public CertificateInfo ParsePfx(byte[] pfxData, string? password = null)
    {
        var cert = new X509Certificate2(pfxData, password ?? "", X509KeyStorageFlags.DefaultKeySet);
        return ExtractInfo(cert);
    }

    /// <summary>Loads a certificate from a file (auto-detects PEM vs PFX by extension).</summary>
    public CertificateInfo LoadFromFile(string path, string? password = null)
    {
        if (!File.Exists(path))
            throw new ValidationException($"Certificate file not found: {path}");

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pem" || ext == ".crt" || ext == ".cer")
        {
            var pem = File.ReadAllText(path);
            return ParsePem(pem);
        }
        if (ext == ".pfx" || ext == ".p12")
        {
            var data = File.ReadAllBytes(path);
            return ParsePfx(data, password);
        }
        // Try PEM first, then PFX
        try
        {
            var pem = File.ReadAllText(path);
            return ParsePem(pem);
        }
        catch
        {
            var data = File.ReadAllBytes(path);
            return ParsePfx(data, password);
        }
    }

    /// <summary>Exports a certificate as PEM format.</summary>
    public string ExportPem(X509Certificate2 cert)
    {
        return cert.ExportCertificatePem();
    }

    /// <summary>Exports a certificate + private key as PFX.</summary>
    public byte[] ExportPfx(X509Certificate2 cert, string password)
    {
        return cert.Export(X509ContentType.Pfx, password);
    }

    /// <summary>Checks if a certificate is expired or expiring soon.</summary>
    public static (bool IsExpired, bool IsExpiringSoon, int DaysRemaining) CheckExpiry(CertificateInfo info)
    {
        var now = DateTimeOffset.UtcNow;
        var days = (info.NotAfter - now).Days;
        return (days < 0, days >= 0 && days <= 30, days);
    }

    private static CertificateInfo ExtractInfo(X509Certificate2 cert)
    {
        return new CertificateInfo
        {
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            Thumbprint = cert.Thumbprint,
            SerialNumber = cert.SerialNumber,
            NotBefore = cert.NotBefore,
            NotAfter = cert.NotAfter,
            KeyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault()?.KeyUsages.ToString() ?? "",
            HasPrivateKey = cert.HasPrivateKey,
            RawData = cert.RawData,
        };
    }
}

/// <summary>Metadata extracted from an X.509 certificate.</summary>
public sealed class CertificateInfo
{
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public string KeyUsage { get; set; } = "";
    public bool HasPrivateKey { get; set; }
    public byte[] RawData { get; set; } = Array.Empty<byte>();

    public string CommonName => ExtractCommonName(Subject);

    private static string ExtractCommonName(string subject)
    {
        // Subject format: "CN=example.com, O=Org, C=US"
        var parts = subject.Split(',', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
            if (part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return part[3..];
        return subject;
    }
}
