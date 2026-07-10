using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V03;

/// <summary>v0.3 S5-T6: KeePassXmlImporter tests. Validates the standard
/// KeePass 2.x XML export shape (Title / UserName / Password / URL / Notes
/// + CustomProperties).</summary>
public class KeePassXmlImporterTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public KeePassXmlImporterTests(TempVaultDir dir) { _dir = dir; }

    private (KeePassXmlImporter imp, VaultService vault, LockService ls) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var entries = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), _crypto);
        return (new KeePassXmlImporter(entries, vs, _crypto), vs, ls);
    }

    [Fact]
    public async Task Import_BasicEntry_ConvertsToOmniKeyEntry()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeePassFile>
  <Root>
    <Group>
      <Name>Root</Name>
      <Entry>
        <String><Key>Title</Key><Value>My OpenAI key</Value></String>
        <String><Key>UserName</Key><Value>me@example.com</Value></String>
        <String><Key>Password</Key><Value>sk-secret-123</Value></String>
        <String><Key>URL</Key><Value>https://platform.openai.com</Value></String>
        <String><Key>Notes</Key><Value>production</Value></String>
      </Entry>
    </Group>
  </Root>
</KeePassFile>";

            var result = imp.ImportFromString("prod", xml);
            result.EntriesImported.Should().Be(1);
            result.EntriesSkipped.Should().Be(0);
            var entries = vault.ListEntries("prod");
            entries.Should().HaveCount(1);
            entries[0].Name.Should().Be("My OpenAI key");
            entries[0].Type.Should().Be(EntryType.ApiKey);
            entries[0].PlatformId.Should().Be("openai");
            entries[0].Notes.Should().Be("production");
            entries[0].Fields.Should().Contain(f => f.Key == "username" && f.ValueString == "me@example.com");
            entries[0].Fields.Should().Contain(f => f.Key == "password" && f.ValueString == "sk-secret-123");
            entries[0].Fields.Should().Contain(f => f.Key == "url" && f.ValueString == "https://platform.openai.com");
        }
    }

    [Fact]
    public async Task Import_100Entries_AllSucceed()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?><KeePassFile><Root><Group><Name>Root</Name>");
            for (int i = 0; i < 100; i++)
            {
                sb.Append($@"<Entry>
                    <String><Key>Title</Key><Value>Entry {i}</Value></String>
                    <String><Key>UserName</Key><Value>user{i}@x.com</Value></String>
                    <String><Key>Password</Key><Value>pwd-{i}</Value></String>
                </Entry>");
            }
            sb.AppendLine("</Group></Root></KeePassFile>");

            var result = imp.ImportFromString("prod", sb.ToString());
            result.EntriesImported.Should().Be(100);
            vault.ListEntries("prod").Should().HaveCount(100);
        }
    }

    [Fact]
    public async Task Import_CustomTOTPProperty_PromotesToTotpUri()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeePassFile>
  <Root><Group><Name>R</Name>
    <Entry>
      <String><Key>Title</Key><Value>Test</Value></String>
      <CustomProperties>
        <Property><Key>otp</Key><Value>otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP</Value></Property>
        <Property><Key>region</Key><Value>us-west-2</Value></Property>
      </CustomProperties>
    </Entry>
  </Group></Root>
</KeePassFile>";

            var result = imp.ImportFromString("prod", xml);
            result.EntriesImported.Should().Be(1);
            var e = vault.ListEntries("prod")[0];
            e.Fields.Should().Contain(f => f.Key == "otp" && f.Kind == FieldKind.TotpUri);
            e.Fields.Should().Contain(f => f.Key == "region" && f.ValueString == "us-west-2");
        }
    }

    [Fact]
    public async Task Import_NoteOnlyEntry_TypeIsNote()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeePassFile><Root><Group><Name>R</Name>
  <Entry>
    <String><Key>Title</Key><Value>Just a note</Value></String>
    <String><Key>Notes</Key><Value>no password here</Value></String>
  </Entry>
</Group></Root></KeePassFile>";

            imp.ImportFromString("prod", xml);
            vault.ListEntries("prod")[0].Type.Should().Be(EntryType.Note);
        }
    }

    [Fact]
    public async Task Import_EmptyTitle_Skips()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeePassFile><Root><Group><Name>R</Name>
  <Entry>
    <String><Key>Title</Key><Value></Value></String>
    <String><Key>Password</Key><Value>x</Value></String>
  </Entry>
</Group></Root></KeePassFile>";

            var result = imp.ImportFromString("prod", xml);
            result.EntriesSkipped.Should().Be(1);
            result.EntriesImported.Should().Be(0);
        }
    }

    [Fact]
    public async Task Import_InvalidXml_ThrowsValidation()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            Assert.Throws<ValidationException>(() => imp.ImportFromString("prod", "not xml at all"));
        }
    }

    [Fact]
    public async Task Import_NoEntries_ThrowsValidation()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?><KeePassFile><Root><Group><Name>Empty</Name></Group></Root></KeePassFile>";
            Assert.Throws<ValidationException>(() => imp.ImportFromString("prod", xml));
        }
    }

    [Fact]
    public async Task Import_PlatformId_ExtractedFromUrl()
    {
        var (imp, vault, ls) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "test",
                System.Text.Encoding.UTF8.GetBytes("pw"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeePassFile><Root><Group><Name>R</Name>
  <Entry>
    <String><Key>Title</Key><Value>GH</Value></String>
    <String><Key>URL</Key><Value>https://github.com/settings/tokens</Value></String>
  </Entry>
  <Entry>
    <String><Key>Title</Key><Value>Stripe</Value></String>
    <String><Key>URL</Key><Value>https://dashboard.stripe.com/apikeys</Value></String>
  </Entry>
  <Entry>
    <String><Key>Title</Key><Value>Random</Value></String>
    <String><Key>URL</Key><Value>https://example.com/</Value></String>
  </Entry>
</Group></Root></KeePassFile>";

            imp.ImportFromString("prod", xml);
            var entries = vault.ListEntries("prod");
            entries.Should().Contain(e => e.Name == "GH" && e.PlatformId == "github");
            entries.Should().Contain(e => e.Name == "Stripe" && e.PlatformId == "stripe");
            entries.Should().Contain(e => e.Name == "Random" && e.PlatformId == null);
        }
    }
}
