using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for EnvFileService: .env file import and export.
/// </summary>
public class EnvFileServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public EnvFileServiceTests(TempVaultDir dir) => _dir = dir;

    private (EnvFileService svc, VaultService vault, LockService ls) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var entries = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), _crypto);
        return (new EnvFileService(entries, vs, _crypto), vs, ls);
    }

    private async Task SetupVault(VaultService vault)
    {
        await vault.CreateAsync(_dir.RandomPath(), "env-test",
            Encoding.UTF8.GetBytes("pw"),
            Argon2Params.ForTests(32 * 1024 * 1024));
    }

    [Fact]
    public async Task ImportFromString_BasicEnv_CreatesEntry()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var env = "DATABASE_URL=postgres://localhost:5432/mydb\n" +
                      "API_KEY=sk-test-12345\n" +
                      "PORT=3000";

            var count = svc.ImportFromString("prod", env, "My App Config");
            count.Should().Be(1);

            var entries = vault.ListEntries("prod");
            entries.Should().HaveCount(1);
            entries[0].Name.Should().Be("My App Config");
            entries[0].Fields.Should().Contain(f => f.Key == "database_url" && f.ValueString == "postgres://localhost:5432/mydb");
            entries[0].Fields.Should().Contain(f => f.Key == "api_key" && f.ValueString == "sk-test-12345" && f.Sensitive);
            entries[0].Fields.Should().Contain(f => f.Key == "port" && f.ValueString == "3000" && !f.Sensitive);
        }
    }

    [Fact]
    public async Task ImportFromString_WithComments_SkipsComments()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var env = "# This is a comment\n" +
                      "KEY1=value1\n" +
                      "# Another comment\n" +
                      "KEY2=value2";

            var count = svc.ImportFromString("prod", env);
            count.Should().Be(1);

            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().HaveCount(2);
            entries[0].Fields.Should().Contain(f => f.Key == "key1");
            entries[0].Fields.Should().Contain(f => f.Key == "key2");
        }
    }

    [Fact]
    public async Task ImportFromString_QuotedValues_StripsQuotes()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var env = "KEY1=\"quoted-value\"\n" +
                      "KEY2='single-quoted'";

            svc.ImportFromString("prod", env);
            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().Contain(f => f.Key == "key1" && f.ValueString == "quoted-value");
            entries[0].Fields.Should().Contain(f => f.Key == "key2" && f.ValueString == "single-quoted");
        }
    }

    [Fact]
    public async Task ImportFromString_SensitiveKeys_MarkedSensitive()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var env = "PASSWORD=mypassword\n" +
                      "SECRET_KEY=mysecret\n" +
                      "API_TOKEN=mytoken\n" +
                      "CREDENTIAL=mycredential\n" +
                      "NORMAL_VAR=normalvalue";

            svc.ImportFromString("prod", env);
            var entries = vault.ListEntries("prod");
            entries[0].Fields.First(f => f.Key == "password").Sensitive.Should().BeTrue();
            entries[0].Fields.First(f => f.Key == "secret_key").Sensitive.Should().BeTrue();
            entries[0].Fields.First(f => f.Key == "api_token").Sensitive.Should().BeTrue();
            entries[0].Fields.First(f => f.Key == "credential").Sensitive.Should().BeTrue();
            entries[0].Fields.First(f => f.Key == "normal_var").Sensitive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ImportFromString_EmptyContent_ReturnsZero()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            svc.ImportFromString("prod", "").Should().Be(0);
            svc.ImportFromString("prod", "# only comments\n# more comments").Should().Be(0);
        }
    }

    [Fact]
    public async Task ImportFromString_KeyNormalization_LowercaseUnderscore()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var env = "DATABASE URL=postgres://localhost\n" +
                      "API Key=sk-test";

            svc.ImportFromString("prod", env);
            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().Contain(f => f.Key == "database_url");
            entries[0].Fields.Should().Contain(f => f.Key == "api_key");
        }
    }

    [Fact]
    public async Task ExportToString_ProducesValidEnvFormat()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var env = "API_KEY=sk-test-123\nPORT=3000";
            svc.ImportFromString("prod", env, "TestApp");

            var exported = svc.ExportToString("prod");
            exported.Should().Contain("API_KEY=sk-test-123");
            exported.Should().Contain("PORT=3000");
            exported.Should().Contain("# Exported from OmniKey Vault");
        }
    }

    [Fact]
    public async Task ExportAsync_WritesFile()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            svc.ImportFromString("prod", "KEY1=val1\nKEY2=val2", "TestApp");

            var exportPath = Path.Combine(_dir.Root, "export.env");
            await svc.ExportAsync("prod", exportPath);

            File.Exists(exportPath).Should().BeTrue();
            var content = await File.ReadAllTextAsync(exportPath);
            content.Should().Contain("KEY1=val1");
            content.Should().Contain("KEY2=val2");
        }
    }

    [Fact]
    public async Task ImportAsync_FromFile_ImportsCorrectly()
    {
        var (svc, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var envPath = Path.Combine(_dir.Root, "test.env");
            await File.WriteAllTextAsync(envPath, "API_KEY=sk-test\nPORT=8080");

            var count = await svc.ImportAsync("prod", envPath, "FromFileTest");
            count.Should().Be(1);

            var entries = vault.ListEntries("prod");
            entries[0].Fields.Should().Contain(f => f.Key == "api_key" && f.ValueString == "sk-test");
        }
    }
}
