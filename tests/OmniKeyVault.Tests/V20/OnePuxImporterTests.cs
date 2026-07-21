using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for OnePuxImporter: 1Password .1pux native format import.
/// Creates test .1pux ZIP files in-memory.
/// </summary>
public class OnePuxImporterTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public OnePuxImporterTests(TempVaultDir dir) => _dir = dir;

    private (OnePuxImporter importer, VaultService vault, LockService ls) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var entries = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), _crypto);
        return (new OnePuxImporter(entries, vs, _crypto), vs, ls);
    }

    private async Task SetupVault(VaultService vault)
    {
        await vault.CreateAsync(_dir.RandomPath(), "1pux-test",
            Encoding.UTF8.GetBytes("pw"),
            Argon2Params.ForTests(32 * 1024 * 1024));
    }

    private string CreateTest1PuxFile()
    {
        var json = """{"accounts":[{"vaults":[{"items":[{"title":"OpenAI API Key","category":"LOGIN","notes":"Production key","urls":[{"href":"https://api.openai.com"}],"fields":[{"id":"username","label":"Username","value":"me@test.com","purpose":""},{"id":"password","label":"Password","value":"sk-test-key-123","purpose":"PASSWORD"}]},{"title":"GitHub Token","category":"LOGIN","notes":"","urls":[{"href":"https://github.com"}],"fields":[{"id":"password","label":"Token","value":"ghp_token123","purpose":"PASSWORD"}]},{"title":"My Secure Note","category":"SECURE_NOTE","notes":"This is a note","urls":[],"fields":[]}]}]}]}""";

        var path = Path.Combine(_dir.Root, $"test-{Guid.NewGuid():N}.1pux");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("export.data");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(json);
        }
        return path;
    }

    [Fact]
    public async Task Import_Valid1Pux_ImportsAllItems()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var puxPath = CreateTest1PuxFile();

            var count = await imp.ImportAsync("prod", puxPath);
            count.Should().Be(3);

            var entries = vault.ListEntries("prod");
            entries.Should().HaveCount(3);
            entries.Should().Contain(e => e.Name == "OpenAI API Key");
            entries.Should().Contain(e => e.Name == "GitHub Token");
            entries.Should().Contain(e => e.Name == "My Secure Note");
        }
    }

    [Fact]
    public async Task Import_LoginItem_TypeIsApiKey()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var puxPath = CreateTest1PuxFile();

            await imp.ImportAsync("prod", puxPath);

            var entries = vault.ListEntries("prod");
            var loginEntry = entries.First(e => e.Name == "OpenAI API Key");
            loginEntry.Type.Should().Be(EntryType.ApiKey);
        }
    }

    [Fact]
    public async Task Import_SecureNote_TypeIsNote()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var puxPath = CreateTest1PuxFile();

            await imp.ImportAsync("prod", puxPath);

            var entries = vault.ListEntries("prod");
            var noteEntry = entries.First(e => e.Name == "My Secure Note");
            noteEntry.Type.Should().Be(EntryType.Note);
        }
    }

    [Fact]
    public async Task Import_PasswordField_MarkedSensitive()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var puxPath = CreateTest1PuxFile();

            await imp.ImportAsync("prod", puxPath);

            var entries = vault.ListEntries("prod");
            var loginEntry = entries.First(e => e.Name == "OpenAI API Key");
            loginEntry.Fields.Should().Contain(f => f.Key == "Password" && f.Sensitive && f.ValueString == "sk-test-key-123");
        }
    }

    [Fact]
    public async Task Import_TagsContain1Password()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var puxPath = CreateTest1PuxFile();

            await imp.ImportAsync("prod", puxPath);

            var entries = vault.ListEntries("prod");
            entries.Should().AllSatisfy(e => e.Tags.Should().Contain("1password"));
            entries.Should().AllSatisfy(e => e.Tags.Should().Contain("imported"));
        }
    }

    [Fact]
    public async Task Import_NonExistentFile_Throws()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var act = async () => await imp.ImportAsync("prod", Path.Combine(_dir.Root, "nonexistent.1pux"));
            await act.Should().ThrowAsync<ValidationException>();
        }
    }

    [Fact]
    public async Task Import_InvalidArchive_NoExportData_Throws()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var badPath = Path.Combine(_dir.Root, "bad.1pux");
            using (var archive = ZipFile.Open(badPath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("wrong-file.txt");
            }

            var act = async () => await imp.ImportAsync("prod", badPath);
            await act.Should().ThrowAsync<ValidationException>("archive without export.data should be rejected");
        }
    }

    [Fact]
    public async Task Import_UrlExtractedAsPlatformId()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var puxPath = CreateTest1PuxFile();

            await imp.ImportAsync("prod", puxPath);

            var entries = vault.ListEntries("prod");
            var openaiEntry = entries.First(e => e.Name == "OpenAI API Key");
            openaiEntry.PlatformId.Should().Be("api.openai.com");
        }
    }
}
