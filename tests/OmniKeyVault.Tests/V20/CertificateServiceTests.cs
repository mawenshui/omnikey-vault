using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for CertificateService: PEM parsing, expiry checking, and export.
/// Uses dynamically generated test certificates (no external dependencies).
/// </summary>
public class CertificateServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly CertificateService _svc = new();

    public CertificateServiceTests(TempVaultDir dir) => _dir = dir;

    private static (X509Certificate2 cert, string pem) GenerateTestCert(int validDays = 365)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test.example.com,O=Test Org,C=US", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(validDays));
        var pem = cert.ExportCertificatePem();
        return (cert, pem);
    }

    [Fact]
    public void ParsePem_ValidCertificate_ReturnsInfo()
    {
        var (_, pem) = GenerateTestCert();
        var info = _svc.ParsePem(pem);

        info.Subject.Should().Contain("CN=test.example.com");
        info.Issuer.Should().Contain("CN=test.example.com", "self-signed cert has same issuer");
        info.Thumbprint.Should().NotBeNullOrEmpty();
        info.SerialNumber.Should().NotBeNullOrEmpty();
        info.NotBefore.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(-1), TimeSpan.FromHours(1));
        info.NotAfter.Should().BeAfter(DateTimeOffset.UtcNow);
        // Note: PEM export (ExportCertificatePem) does not include the private key,
        // so CreateFromPem creates a cert without HasPrivateKey.
        // The cert data (subject, issuer, thumbprint, etc.) should still be correct.
        info.RawData.Should().NotBeEmpty();
        info.RawData.Should().NotBeEmpty();
    }

    [Fact]
    public void ParsePem_CommonName_Extracted()
    {
        var (_, pem) = GenerateTestCert();
        var info = _svc.ParsePem(pem);
        info.CommonName.Should().Be("test.example.com");
    }

    [Fact]
    public void ParsePem_KeyUsage_Extracted()
    {
        var (_, pem) = GenerateTestCert();
        var info = _svc.ParsePem(pem);
        info.KeyUsage.Should().NotBeEmpty();
        info.KeyUsage.Should().Contain("DigitalSignature");
    }

    [Fact]
    public void ParsePem_InvalidPem_Throws()
    {
        var act = () => _svc.ParsePem("not a valid PEM");
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParsePfx_ValidPfx_ReturnsInfo()
    {
        var (cert, _) = GenerateTestCert();
        var pfxData = cert.Export(X509ContentType.Pfx, "test-password");
        var info = _svc.ParsePfx(pfxData, "test-password");

        info.Subject.Should().Contain("CN=test.example.com");
        info.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void ParsePfx_WrongPassword_Throws()
    {
        var (cert, _) = GenerateTestCert();
        var pfxData = cert.Export(X509ContentType.Pfx, "correct-password");
        var act = () => _svc.ParsePfx(pfxData, "wrong-password");
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void LoadFromFile_PemFile_ReturnsInfo()
    {
        var (_, pem) = GenerateTestCert();
        var path = Path.Combine(_dir.Root, "test.pem");
        File.WriteAllText(path, pem);

        var info = _svc.LoadFromFile(path);
        info.Subject.Should().Contain("CN=test.example.com");
    }

    [Fact]
    public void LoadFromFile_PfxFile_ReturnsInfo()
    {
        var (cert, _) = GenerateTestCert();
        var pfxData = cert.Export(X509ContentType.Pfx, "pw");
        var path = Path.Combine(_dir.Root, "test.pfx");
        File.WriteAllBytes(path, pfxData);

        var info = _svc.LoadFromFile(path, "pw");
        info.Subject.Should().Contain("CN=test.example.com");
    }

    [Fact]
    public void LoadFromFile_NonExistentFile_Throws()
    {
        var act = () => _svc.LoadFromFile(Path.Combine(_dir.Root, "nonexistent.pem"));
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void CheckExpiry_NotExpired_ReturnsCorrectFlags()
    {
        var (_, pem) = GenerateTestCert(365);
        var info = _svc.ParsePem(pem);

        var (isExpired, isExpiringSoon, daysRemaining) = CertificateService.CheckExpiry(info);
        isExpired.Should().BeFalse();
        daysRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CheckExpiry_AlreadyExpired_ReturnsExpiredTrue()
    {
        var (_, pem) = GenerateTestCert(-1); // expired yesterday
        var info = _svc.ParsePem(pem);

        var (isExpired, _, _) = CertificateService.CheckExpiry(info);
        isExpired.Should().BeTrue();
    }

    [Fact]
    public void CheckExpiry_ExpiringSoon_ReturnsExpiringSoonTrue()
    {
        var (_, pem) = GenerateTestCert(15); // expires in 15 days
        var info = _svc.ParsePem(pem);

        var (isExpired, isExpiringSoon, daysRemaining) = CertificateService.CheckExpiry(info);
        isExpired.Should().BeFalse();
        isExpiringSoon.Should().BeTrue();
        daysRemaining.Should().BeLessThanOrEqualTo(30).And.BeGreaterThan(0);
    }

    [Fact]
    public void ExportPem_ReturnsValidPemString()
    {
        var (cert, _) = GenerateTestCert();
        var pem = _svc.ExportPem(cert);
        pem.Should().StartWith("-----BEGIN CERTIFICATE-----");
        pem.Should().EndWith("-----END CERTIFICATE-----");
    }

    [Fact]
    public void ExportPfx_ReturnsValidPfxData()
    {
        var (cert, _) = GenerateTestCert();
        var pfxData = _svc.ExportPfx(cert, "export-pw");
        pfxData.Should().NotBeEmpty();

        // Verify the exported PFX can be re-imported
        var reimported = new X509Certificate2(pfxData, "export-pw");
        reimported.Subject.Should().Contain("CN=test.example.com");
    }
}
