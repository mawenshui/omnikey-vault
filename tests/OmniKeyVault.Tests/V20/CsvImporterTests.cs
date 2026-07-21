using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for CsvImporter: LastPass/Chrome/Edge/Firefox CSV format detection,
/// field mapping, quoted fields, and empty input handling.
/// </summary>
public class CsvImporterTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public CsvImporterTests(TempVaultDir dir) => _dir = dir;

    private (CsvImporter importer, VaultService vault, LockService ls) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var entries = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), _crypto);
        return (new CsvImporter(entries, vs, _crypto), vs, ls);
    }

    private async Task SetupVault(VaultService vault)
    {
        await vault.CreateAsync(_dir.RandomPath(), "csv-test",
            Encoding.UTF8.GetBytes("pw"),
            Argon2Params.ForTests(32 * 1024 * 1024));
    }

    [Fact]
    public async Task Import_LastPassFormat_ImportsCorrectly()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var csv = "name,url,username,password\n" +
                      "OpenAI,https://api.openai.com,me@test.com,sk-test-key-123\n" +
                      "GitHub,https://github.com,user2,ghp_token456";

            var count = imp.ImportFromString("prod", csv);
            count.Should().Be(2);

            var entries = vault.ListEntries("prod");
            entries.Should().HaveCount(2);
            entries.Should().Contain(e => e.Name == "OpenAI");
            entries.Should().Contain(e => e.Name == "GitHub");
        }
    }

    [Fact]
    public async Task Import_ChromeFormat_ImportsCorrectly()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var csv = "name,url,username,password\n" +
                      "TestSite,https://example.com,myuser,mypassword";

            var count = imp.ImportFromString("prod", csv);
            count.Should().Be(1);

            var entries = vault.ListEntries("prod");
            entries[0].Name.Should().Be("TestSite");
            entries[0].Fields.Should().Contain(f => f.Key == "username" && f.ValueString == "myuser");
            entries[0].Fields.Should().Contain(f => f.Key == "password" && f.ValueString == "mypassword" && f.Sensitive);
            entries[0].Fields.Should().Contain(f => f.Key == "url" && f.ValueString == "https://example.com");
        }
    }

    [Fact]
    public async Task Import_BitwardenFormat_ImportsCorrectly()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var csv = "name,login_uri,login_username,login_password,notes\n" +
                      "Bitwarden Entry,https://bw.com,bwuser,bwpwd,Test note";

            var count = imp.ImportFromString("prod", csv);
            count.Should().Be(1);

            var entries = vault.ListEntries("prod");
            entries[0].Name.Should().Be("Bitwarden Entry");
            entries[0].Fields.Should().Contain(f => f.Key == "username" && f.ValueString == "bwuser");
            entries[0].Fields.Should().Contain(f => f.Key == "password" && f.ValueString == "bwpwd" && f.Sensitive);
        }
    }

    [Fact]
    public async Task Import_QuotedFields_HandledCorrectly()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var csv = "name,url,username,password\n" +
                      "\"My, Site\",\"https://example.com\",\"user,name\",\"pass,word\"";

            var count = imp.ImportFromString("prod", csv);
            count.Should().Be(1);

            var entries = vault.ListEntries("prod");
            entries[0].Name.Should().Be("My, Site");
            entries[0].Fields.Should().Contain(f => f.Key == "username" && f.ValueString == "user,name");
            entries[0].Fields.Should().Contain(f => f.Key == "password" && f.ValueString == "pass,word");
        }
    }

    [Fact]
    public async Task Import_EmptyCsv_ReturnsZero()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            imp.ImportFromString("prod", "").Should().Be(0);
        }
    }

    [Fact]
    public async Task Import_HeaderOnly_ReturnsZero()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            imp.ImportFromString("prod", "name,url,username,password").Should().Be(0);
        }
    }

    [Fact]
    public async Task Import_NoRecognizableColumns_Throws()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);
            var act = () => imp.ImportFromString("prod", "foo,bar\nval1,val2");
            act.Should().Throw<ValidationException>();
        }
    }

    [Fact]
    public async Task Import_EmptyRows_Skipped()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var csv = "name,url,username,password\n" +
                      "Entry1,https://a.com,u1,p1\n" +
                      ",,,\n" +
                      "Entry2,https://b.com,u2,p2";

            var count = imp.ImportFromString("prod", csv);
            count.Should().Be(2, "empty rows should be skipped");
        }
    }

    [Fact]
    public async Task Import_WithNotes_ImportsNotes()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var csv = "name,url,username,password,notes\n" +
                      "TestEntry,https://test.com,user,pass,This is a note";

            imp.ImportFromString("prod", csv);
            var entries = vault.ListEntries("prod");
            entries[0].Notes.Should().Be("This is a note");
        }
    }

    [Fact]
    public async Task Import_TagsContainImported()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var csv = "name,url,username,password\n" +
                      "TestEntry,https://test.com,user,pass";

            imp.ImportFromString("prod", csv);
            var entries = vault.ListEntries("prod");
            entries[0].Tags.Should().Contain("imported");
        }
    }

    [Fact]
    public async Task Import_50Entries_AllSucceed()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await SetupVault(vault);

            var sb = new StringBuilder("name,url,username,password\n");
            for (int i = 0; i < 50; i++)
                sb.AppendLine($"Entry{i},https://site{i}.com,user{i},pass{i}");

            var count = imp.ImportFromString("prod", sb.ToString());
            count.Should().Be(50);
            vault.ListEntries("prod").Should().HaveCount(50);
        }
    }
}
