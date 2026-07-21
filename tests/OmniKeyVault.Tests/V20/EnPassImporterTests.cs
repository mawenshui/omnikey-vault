using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for EnPassImporter: EnPass JSON format import.
/// Tests both flat items format and folder-based format.
/// </summary>
public class EnPassImporterTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public EnPassImporterTests(TempVaultDir dir) => _dir = dir;

    private (EnPassImporter importer, VaultService vault, LockService ls) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var entries = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), _crypto);
        return (new EnPassImporter(entries, vs, _crypto), vs, ls);
    }

    private async Task SetupVault(VaultService vault)
    {
        await vault.CreateAsync(_dir.RandomPath(), "enpass-test",
            Encoding.UTF8.GetBytes("pw"),
            Argon2Params.ForTests(32 * 1024 * 1024));
    }

    [Fact]
    public async Task ImportFromString_FlatItems_ImportsCorrectly()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"items":[{"title":"OpenAI","category":"login","notes":"API key","fields":[{"label":"username","value":"me@test.com","type":"text"},{"label":"password","value":"sk-test-123","type":"password"}]},{"title":"GitHub","category":"login","notes":"","fields":[{"label":"username","value":"user","type":"text"},{"label":"password","value":"ghp_token","type":"password"}]}]}""";

            var count = imp.ImportFromString("prod", json);
            count.Should().Be(2);

            var entries = vault.ListEntries("prod");
            entries.Should().HaveCount(2);
            entries.Should().Contain(e => e.Name == "OpenAI");
            entries.Should().Contain(e => e.Name == "GitHub");
        }
    }

    [Fact]
    public async Task ImportFromString_FolderBased_ImportsCorrectly()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"folders":[{"title":"Work","items":[{"title":"Work Entry","category":"login","fields":[{"label":"password","value":"work-pwd","type":"password"}]}]},{"title":"Personal","items":[{"title":"Personal Entry","category":"login","fields":[{"label":"password","value":"personal-pwd","type":"password"}]}]}]}""";

            var count = imp.ImportFromString("prod", json);
            count.Should().Be(2);

            var entries = vault.ListEntries("prod");
            entries.Should().Contain(e => e.Name == "Work Entry");
            entries.Should().Contain(e => e.Name == "Personal Entry");
        }
    }

    [Fact]
    public async Task ImportFromString_PasswordField_MarkedSensitive()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"items":[{"title":"Test","category":"login","fields":[{"label":"password","value":"secret123","type":"password"}]}]}""";

            imp.ImportFromString("prod", json);
            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().Contain(f => f.Key == "password" && f.Sensitive && f.ValueString == "secret123");
        }
    }

    [Fact]
    public async Task ImportFromString_TotpField_PromotesToTotpUri()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"items":[{"title":"Test","category":"login","fields":[{"label":"otp","value":"otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP","type":"totp"}]}]}""";

            imp.ImportFromString("prod", json);
            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().Contain(f => f.Kind == FieldKind.TotpUri);
        }
    }

    [Fact]
    public async Task ImportFromString_UrlField_PromotesToUrlKind()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"items":[{"title":"Test","category":"login","fields":[{"label":"website","value":"https://example.com","type":"url"}]}]}""";

            imp.ImportFromString("prod", json);
            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().Contain(f => f.Kind == FieldKind.Url);
        }
    }

    [Fact]
    public async Task ImportFromString_SecureNote_TypeIsNote()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"items":[{"title":"My Note","category":"note","fields":[]}]}""";

            imp.ImportFromString("prod", json);
            var entries = vault.ListEntries("prod");
            entries[0].Type.Should().Be(EntryType.Note);
        }
    }

    [Fact]
    public async Task ImportFromString_TagsContainEnPass()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"items":[{"title":"Test","category":"login","fields":[]}]}""";

            imp.ImportFromString("prod", json);
            var entries = vault.ListEntries("prod");
            entries[0].Tags.Should().Contain("enpass");
            entries[0].Tags.Should().Contain("imported");
        }
    }

    [Fact]
    public async Task ImportFromString_EmptyJson_ReturnsZero()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            imp.ImportFromString("prod", """{"items":[]}""").Should().Be(0);
        }
    }

    [Fact]
    public async Task ImportFromString_PinField_MarkedSensitive()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var json = """{"items":[{"title":"Bank","category":"login","fields":[{"label":"PIN","value":"1234","type":"text"}]}]}""";

            imp.ImportFromString("prod", json);
            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().Contain(f => f.Key == "PIN" && f.Sensitive, "field with 'pin' in label should be sensitive");
        }
    }

    [Fact]
    public async Task ImportAsync_FromFile_ImportsCorrectly()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var jsonPath = Path.Combine(_dir.Root, "enpass.json");
            await File.WriteAllTextAsync(jsonPath, """{"items":[{"title":"FileImport","category":"login","fields":[]}]}""");

            var count = await imp.ImportAsync("prod", jsonPath);
            count.Should().Be(1);
        }
    }
}
