using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V03;

/// <summary>v0.3 S6-T4: AttachmentService tests covering encrypt/decrypt
/// round-trip, blob-id stability, file-size accounting, and delete.</summary>
public class AttachmentServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly AttachmentService _svc;
    private readonly KeyEncryptionKey _kek;
    private readonly MasterKey _mk;

    public AttachmentServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "okv-att-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _svc = new AttachmentService(_crypto, _tmpDir);
        // Derive a real KEK from a fake master key (test-only Argon2id with
        // low memory so this stays fast).
        var salt = _crypto.RandomBytes(16);
        _mk = _crypto.DeriveMasterKey(System.Text.Encoding.UTF8.GetBytes("test-pw"), salt,
            Argon2Params.ForTests(8 * 1024 * 1024));
        _kek = _crypto.DeriveKek(_mk, System.Text.Encoding.UTF8.GetBytes("okv-kek-v1"), salt);
    }

    public void Dispose()
    {
        _kek.Dispose();
        _mk.Dispose();
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    [Fact]
    public void Save_ThenRead_RoundTripsPlaintext()
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, attachment world!");
        var id = _svc.Save("hint", plaintext, _kek);
        var read = _svc.Read(id, _kek);
        read.Should().NotBeNull();
        read.Should().Equal(plaintext);
    }

    [Fact]
    public void Save_EmptyPlaintext_Throws()
    {
        Assert.Throws<ValidationException>(() => _svc.Save("h", Array.Empty<byte>(), _kek));
    }

    [Fact]
    public void Save_BlobId_IsSha256OfPlaintext_StableAcrossCalls()
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes("a stable blob");
        var id1 = _svc.Save("h1", plaintext, _kek);
        var id2 = _svc.Save("h2", plaintext, _kek);
        id1.Should().Be(id2);
        id1.Length.Should().Be(64);  // SHA-256 hex
        id1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Read_NonExistentBlob_ReturnsNull()
    {
        _svc.Read("0000000000000000000000000000000000000000000000000000000000000000", _kek)
            .Should().BeNull();
    }

    [Fact]
    public void GetFileSize_ReturnsEncryptedSize()
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes(new string('a', 1024));
        var id = _svc.Save("h", plaintext, _kek);
        var size = _svc.GetFileSize(id);
        // envelope: 24 (kek nonce) + 32 (wrapped dek) + 16 (tag) + 24 (payload nonce) + 1024 (ct) + 16 (tag)
        size.Should().Be(24 + 32 + 16 + 24 + 1024 + 16);
    }

    [Fact]
    public void Delete_RemovesFile_AndReturnsTrue()
    {
        var id = _svc.Save("h", System.Text.Encoding.UTF8.GetBytes("x"), _kek);
        File.Exists(Path.Combine(_tmpDir, id + ".bin")).Should().BeTrue();
        _svc.Delete(id).Should().BeTrue();
        File.Exists(Path.Combine(_tmpDir, id + ".bin")).Should().BeFalse();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _svc.Delete("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void List_ReturnsAllBlobIds()
    {
        _svc.Save("h1", System.Text.Encoding.UTF8.GetBytes("a"), _kek);
        _svc.Save("h2", System.Text.Encoding.UTF8.GetBytes("b"), _kek);
        _svc.Save("h3", System.Text.Encoding.UTF8.GetBytes("c"), _kek);
        var ids = _svc.List();
        ids.Should().HaveCount(3);
    }

    [Fact]
    public void Read_AfterPurgeCache_StillWorks()
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes("persistent!");
        var id = _svc.Save("h", plaintext, _kek);
        _svc.PurgeCache();
        // Even after cache purge, the on-disk encrypted file is still
        // readable (re-decrypt on demand).
        var read = _svc.Read(id, _kek);
        read.Should().Equal(plaintext);
    }

    [Fact]
    public void Save_ThenTamper_ThrowsOnRead()
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes("untouched");
        var id = _svc.Save("h", plaintext, _kek);
        // Corrupt the encrypted file (flip one byte in the payload section)
        var path = Path.Combine(_tmpDir, id + ".bin");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length - 5] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        _svc.PurgeCache();
        Assert.Throws<CryptoException>(() => _svc.Read(id, _kek));
    }

    [Fact]
    public void RoundTrip_LargeBlob_1MB()
    {
        var big = _crypto.RandomBytes(1024 * 1024);
        var id = _svc.Save("h", big, _kek);
        var read = _svc.Read(id, _kek);
        read.Should().Equal(big);
    }
}
