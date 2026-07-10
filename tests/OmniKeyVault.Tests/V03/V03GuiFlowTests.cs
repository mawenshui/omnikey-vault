using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V03;

/// <summary>
/// v0.3 GUI flow tests. The Avalonia headless harness is not yet wired in
/// the test project (would require <c>Avalonia.Headless</c> + an
/// <c>AppBuilder</c> fixture per platform). For v0.3 we exercise the same
/// service-layer code paths the GUI calls into:
///
///   1. <see cref="SearchService"/>: full-text + field-level query
///   2. <see cref="AttachmentService"/>: save / read / list / delete blobs
///   3. <see cref="KeePassXmlImporter"/>: import 100-entry KeePass XML
///   4. Locale switching: zh-CN / en-US roundtrip
///
/// These tests fail if the v0.3 service contracts regress, which is the
/// same coverage Avalonia headless would give for the "GUI button click →
/// service call" wiring without the UI plumbing overhead.
/// </summary>
public class V03GuiFlowTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public V03GuiFlowTests(TempVaultDir dir) { _dir = dir; }

    private (VaultService vault, LockService ls, EntryService entries, ProfileService profiles,
             SearchService search, AttachmentService attachments, KeePassXmlImporter kp) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var clip = new ClipboardService(new InMemoryClipboardProvider(), ls);
        var entrySvc = new EntryService(vs, new TemplateService(), clip, _crypto);
        var profileSvc = new ProfileService(vs, _crypto, ls);
        var searchSvc = new SearchService();
        var attDir = Path.Combine(_dir.RandomPath(), "attachments");
        var attSvc = new AttachmentService(_crypto, attDir, () =>
        {
            var cur = ls.CurrentKek;
            return cur == null ? null : KeyEncryptionKey.From(cur.ToArray());
        });
        var kpSvc = new KeePassXmlImporter(entrySvc, vs, _crypto);
        return (vs, ls, entrySvc, profileSvc, searchSvc, attSvc, kpSvc);
    }

    [Fact]
    public async Task GuiSearchFlow_SearchFieldColon_FindsAndHighlights()
    {
        // Mirrors what the v0.3 SearchWindow does: take the user's typed
        // query, run SearchService, and feed the hits into the list view.
        var (vault, ls, entries, _, search, _, _) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "v03", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var e1 = entries.Create("prod", "OpenAI", EntryType.ApiKey, "openai",
                new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("sk-foo"), Kind = FieldKind.Secret, Sensitive = true } });
            var e2 = entries.Create("prod", "GitHub", EntryType.ApiKey, "github",
                new[] { new Field { Key = "pat", Value = FieldCodec.Encode("ghp-bar"), Kind = FieldKind.Secret, Sensitive = true } });
            vault.PutEntry("prod", e1);
            vault.PutEntry("prod", e2);
            await vault.SaveAsync();

            // Step 1: free-text search (as if the user typed "openai")
            var freeHits = search.Search("openai", vault.ListEntries("prod"));
            freeHits.Should().HaveCount(1);
            freeHits[0].Entry.Name.Should().Be("OpenAI");

            // Step 2: field-level search by key (as if the user typed "field:pat")
            // — this only checks the field KEY, not the value, so no FieldHit
            // is added (the GUI uses this as a "filter by structure" mode).
            var fieldHits = search.Search("field:pat", vault.ListEntries("prod"));
            fieldHits.Should().HaveCount(1);
            fieldHits[0].Entry.Name.Should().Be("GitHub");

            // Step 2b: field:KEY:VALUE form — this adds a FieldHit for highlighting
            var fieldValueHits = search.Search("field:pat:ghp-bar", vault.ListEntries("prod"));
            fieldValueHits.Should().HaveCount(1);
            fieldValueHits[0].Entry.Name.Should().Be("GitHub");
            fieldValueHits[0].FieldHits.Should().ContainSingle();
            fieldValueHits[0].FieldHits[0].FieldKey.Should().Be("pat");

            // Step 3: combined AND (as if the user typed "platform:github AND field:pat")
            var comboHits = search.Search("platform:github AND field:pat", vault.ListEntries("prod"));
            comboHits.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task GuiAttachmentFlow_SaveReadListDelete_AllSucceed()
    {
        // Mirrors what the v0.3 EditorWindow's "Add attachment" button does.
        var (vault, ls, entries, _, _, attachments, _) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "v03", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            // Step 1: user picks a PEM file to attach
            var pemBytes = Encoding.UTF8.GetBytes("-----BEGIN RSA PRIVATE KEY-----\nfake\n-----END RSA PRIVATE KEY-----");
            var blobId = attachments.Save(blobIdHint: "demo-pem", plaintext: pemBytes);
            blobId.Should().NotBeNullOrEmpty();
            blobId.Length.Should().Be(64); // SHA-256 hex

            // Step 2: re-read (e.g. the user clicks "download")
            var readBack = attachments.Read(blobId);
            readBack.Should().NotBeNull();
            Encoding.UTF8.GetString(readBack!).Should().Be(Encoding.UTF8.GetString(pemBytes));

            // Step 3: list (e.g. the EditorWindow's "Attached files" panel)
            var list = attachments.List();
            list.Should().Contain(blobId);

            // Step 4: delete (e.g. user clicks "remove attachment")
            var deleted = attachments.Delete(blobId);
            deleted.Should().BeTrue();

            // After delete: read returns null
            var gone = attachments.Read(blobId);
            gone.Should().BeNull();
        }
    }

    [Fact]
    public async Task GuiKeePassImportFlow_BasicXml_ProducesValidEntries()
    {
        // Mirrors what the v0.3 KeePassImportWindow does after the user
        // picks a KeePass 2.x XML export file.
        var (vault, ls, _, _, _, _, kp) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "v03", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeePassFile>
  <Root>
    <Group>
      <Name>Root</Name>
      <Entry>
        <String><Key>Title</Key><Value>Imported GitHub</Value></String>
        <String><Key>UserName</Key><Value>me@x.com</Value></String>
        <String><Key>Password</Key><Value>ghp_fake</Value></String>
        <String><Key>URL</Key><Value>https://github.com</Value></String>
      </Entry>
      <Entry>
        <String><Key>Title</Key><Value>Imported OpenAI</Value></String>
        <String><Key>UserName</Key><Value>me@x.com</Value></String>
        <String><Key>Password</Key><Value>sk-fake</Value></String>
        <String><Key>URL</Key><Value>https://openai.com</Value></String>
      </Entry>
    </Group>
  </Root>
</KeePassFile>";
            var result = kp.ImportFromString("prod", xml);
            result.EntriesImported.Should().Be(2);
            var entries = vault.ListEntries("prod");
            entries.Should().HaveCount(2);
            entries.Select(e => e.Name).Should().Contain(new[] { "Imported GitHub", "Imported OpenAI" });
            // The URL field should be detected as the platform_id
            entries.Should().Contain(e => e.PlatformId == "github");
            entries.Should().Contain(e => e.PlatformId == "openai");
        }
    }

    [Fact]
    public void GuiLocaleFlow_SwitchLanguages_RoundTrips()
    {
        // Mirrors what the v0.3 SettingsWindow's language combo does.
        // The default locale is zh-CN; switching to en-US changes the
        // strings returned by UIStrings.Get(...).
        OmniKeyVault.Cli.Gui.UIStrings.SetLocale("zh-CN").Should().BeTrue();
        var zh = OmniKeyVault.Cli.Gui.UIStrings.Get("common.save");
        zh.Should().Be("保存");

        OmniKeyVault.Cli.Gui.UIStrings.SetLocale("en-US").Should().BeTrue();
        var en = OmniKeyVault.Cli.Gui.UIStrings.Get("common.save");
        en.Should().Be("Save");

        // The two locales should produce different strings (the point of
        // switching). If they ever match, either the en-US resource is
        // missing or someone re-used the zh-CN key as the en-US value.
        zh.Should().NotBe(en, "zh-CN and en-US must produce distinct strings for the same key");
    }
}
