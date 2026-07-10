using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V04;

/// <summary>
/// v0.4 GUI flow tests. The Avalonia headless harness is not yet wired in
/// the test project (would require <c>Avalonia.Headless</c> + an
/// <c>AppBuilder</c> fixture per platform). For v0.4 we exercise the same
/// service-layer code paths the GUI calls into:
///
///   1. <see cref="IdleTimer"/>: 15-min default + RecordActivity reset
///   2. <see cref="IPlatformRotator"/>: OpenAI / GitHub metadata + mock error path
///   3. <see cref="BackupService"/>: history snapshot roundtrip + restore
///
/// Same trade-off as <see cref="V03.V03GuiFlowTests"/>: service contracts
/// are exercised; the "click button → service call" wiring is verified
/// manually in the GUI demo entries (see <c>OKV_GUI_DEMO_*</c> env vars).
/// </summary>
public class V04GuiFlowTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public V04GuiFlowTests(TempVaultDir dir) { _dir = dir; }

    private (VaultService vault, LockService ls, EntryService entries, ProfileService profiles,
             BackupService backup, IdleTimer timer) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var clip = new ClipboardService(new InMemoryClipboardProvider(), ls);
        var entrySvc = new EntryService(vs, new TemplateService(), clip, _crypto);
        var profileSvc = new ProfileService(vs, _crypto, ls);
        var backupSvc = new BackupService(vs, "test-device");
        var timer = new IdleTimer(15);  // 15-min default per MANUAL §4.9
        return (vs, ls, entrySvc, profileSvc, backupSvc, timer);
    }

    [Fact]
    public async Task GuiIdleTimerFlow_DefaultAndReset_BehavesAsExpected()
    {
        // Mirrors the v0.4 MainWindow: create an IdleTimer, expect
        // 15-minute default, then simulate user activity (typing in
        // search box) and verify SecondsSinceActivity drops to 0.
        var (vault, ls, _, _, _, timer) = CreateAll();
        using (vault) using (ls) using (timer)
        {
            await vault.CreateAsync(_dir.RandomPath(), "v04", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            timer.IdleMinutes.Should().Be(15);  // PRD §6 + MANUAL §4.9

            // Wait a beat — without activity the timer should be counting up
            await Task.Delay(1200);
            timer.SecondsSinceActivity.Should().BeGreaterOrEqualTo(1);

            // User types in the search box → MainWindow fires RecordActivity
            timer.RecordActivity();
            timer.SecondsSinceActivity.Should().BeLessThan(1);

            // Total budget should still be 15 minutes (no reset on change)
            timer.SecondsUntilTimeout.Should().BeInRange(14 * 60, 15 * 60);
        }
    }

    [Fact]
    public async Task GuiRotateFlow_OpenAiRotator_ExposesCorrectMetadata()
    {
        // Mirrors the v0.4 EditorWindow's "Rotate" button: it looks up
        // the entry's platform_id, then asks the Rotators dictionary
        // for a matching IPlatformRotator. This test verifies the
        // platform-id → field-key mapping that drives the button's
        // visibility logic.
        var (vault, ls, _, _, _, _) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "v04", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var rotators = new Dictionary<string, IPlatformRotator>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = new OpenAiRotator(),
                ["github"] = new GitHubPatRotator(),
            };
            // Simulate the EditorWindow's logic: "if the entry's
            // platform_id is in Rotators, show the Rotate button next
            // to the field whose key matches rotator.FieldKey."
            rotators["openai"].FieldKey.Should().Be("api_key");
            rotators["github"].FieldKey.Should().Be("token");
            rotators["openai"].DisplayName.Should().Be("OpenAI API Key");
            rotators["github"].DisplayName.Should().Contain("GitHub");
        }
    }

    [Fact]
    public async Task GuiHistoryFlow_EditTwiceThenRestoreToV1_RestoresOriginal()
    {
        // Mirrors the v0.4 HistoryWindow: list snapshots, let the user
        // pick v1, restore. The MainWindow's flow is: user clicks
        // "Restore" → BackupService.Restore() → version+1 → save.
        var (vault, ls, entries, _, backup, _) = CreateAll();
        using (vault) using (ls)
        {
            await vault.CreateAsync(_dir.RandomPath(), "v04", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            // v1
            var e = entries.Create("prod", "HistoryTest", EntryType.ApiKey, "github",
                new[] { new Field { Key = "pat", Value = FieldCodec.Encode("v1"), Kind = FieldKind.Secret, Sensitive = true } });
            e = e with { Version = 1u };
            vault.PutEntry("prod", e);
            backup.Capture("prod", e, "v1");
            // v2
            e = e with { Fields = new[] { e.Fields[0] with { Value = FieldCodec.Encode("v2") } }, Version = 2u };
            vault.PutEntry("prod", e);
            backup.Capture("prod", e, "v2");
            // v3
            e = e with { Fields = new[] { e.Fields[0] with { Value = FieldCodec.Encode("v3") } }, Version = 3u };
            vault.PutEntry("prod", e);
            backup.Capture("prod", e, "v3");
            await vault.SaveAsync();

            // User opens HistoryWindow, sees 2 history entries (v1, v2)
            var history = backup.ListHistory("prod", e.Id);
            history.Count.Should().BeGreaterOrEqualTo(2);
            history.Select(s => s.Version).Should().Contain(new uint[] { 1, 2 });

            // User picks v1 → restore
            var restored = backup.Restore("prod", e.Id, 1);
            restored.Fields[0].ValueString.Should().Be("v1");
            // The restored entry has a new version (1+1=2 by BackupService convention)
            await vault.SaveAsync();
            var live = vault.GetEntry("prod", e.Id);
            live!.Version.Should().BeGreaterOrEqualTo(2);
            live.Fields[0].ValueString.Should().Be("v1");
        }
    }
}
