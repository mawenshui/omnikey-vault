using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for KeePassKdbxImporter: KDBX file signature validation,
/// invalid file handling, and error messages.
/// Full KDBX decryption is tested via integration with actual KeePass databases.
/// </summary>
public class KeePassKdbxImporterTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public KeePassKdbxImporterTests(TempVaultDir dir) => _dir = dir;

    private (KeePassKdbxImporter importer, VaultService vault, LockService ls) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var entries = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), _crypto);
        var xmlImporter = new KeePassXmlImporter(entries, vs, _crypto);
        return (new KeePassKdbxImporter(xmlImporter), vs, ls);
    }

    private async Task SetupVault(VaultService vault)
    {
        await vault.CreateAsync(_dir.RandomPath(), "kdbx-test",
            Encoding.UTF8.GetBytes("pw"),
            Argon2Params.ForTests(32 * 1024 * 1024));
    }

    [Fact]
    public async Task Import_NonExistentFile_Throws()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var act = async () => await imp.ImportAsync("prod", Path.Combine(_dir.Root, "nonexistent.kdbx"), "password");
            await act.Should().ThrowAsync<ValidationException>();
        }
    }

    [Fact]
    public async Task Import_InvalidSignature_Throws()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var fakePath = Path.Combine(_dir.Root, "fake.kdbx");
            await File.WriteAllBytesAsync(fakePath, new byte[100]); // all zeros, invalid signature

            var act = async () => await imp.ImportAsync("prod", fakePath, "password");
            await act.Should().ThrowAsync<ValidationException>("file with invalid KDBX signature should be rejected");
        }
    }

    [Fact]
    public async Task Import_ShortFile_Throws()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var shortPath = Path.Combine(_dir.Root, "short.kdbx");
            await File.WriteAllBytesAsync(shortPath, new byte[] { 0x01, 0x02, 0x03 });

            var act = async () => await imp.ImportAsync("prod", shortPath, "password");
            await act.Should().ThrowAsync<Exception>();
        }
    }

    [Fact]
    public async Task Import_TextFile_Throws()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var textPath = Path.Combine(_dir.Root, "text.kdbx");
            await File.WriteAllTextAsync(textPath, "This is not a KDBX file");

            var act = async () => await imp.ImportAsync("prod", textPath, "password");
            await act.Should().ThrowAsync<Exception>();
        }
    }
}
