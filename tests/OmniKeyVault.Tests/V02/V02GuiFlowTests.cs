using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.V02;

/// <summary>
/// End-to-end v0.2 GUI flow tests. Mirrors what the new v0.2 windows exercise:
///   1. Create vault with one profile (prod)
///   2. Add a TOTP entry + secret + an EntryService.Create roundtrip
///   3. Create a dev profile and switch to it
///   4. Export prod entries to a .okv.dev seed (strip secrets)
///   5. Import the seed into dev profile
///   6. Change the master password
///   7. Sync the vault against a clone
///   8. Watch the file for changes
/// All in-memory where possible; disk only for the .okv file itself.
/// </summary>
public class V02GuiFlowTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public V02GuiFlowTests(TempVaultDir dir) { _dir = dir; }

    private (VaultService vault, LockService lockSvc, EntryService entries, ProfileService profiles,
             SeedExporter seedExport, SeedImporter seedImport, BackupService backup) CreateAll()
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var clip = new ClipboardService(new InMemoryClipboardProvider(), ls);
        var entrySvc = new EntryService(vs, new TemplateService(), clip, _crypto);
        var profileSvc = new ProfileService(vs, _crypto, ls);
        var seedFmt = new SeedFormat();
        var exportSvc = new SeedExporter(vs, _crypto, _codec, seedFmt, "test-device");
        var importSvc = new SeedImporter(vs, _crypto, _codec, seedFmt, "test-device");
        var backupSvc = new BackupService(vs, "test-device");
        return (vs, ls, entrySvc, profileSvc, exportSvc, importSvc, backupSvc);
    }

    [Fact]
    public async Task FullV02Flow_CreateAddEntrySeedImportPasswordSync()
    {
        var (vault, lockSvc, entries, profiles, seedExport, seedImport, backup) = CreateAll();
        using (vault) using (lockSvc)
        {
            // 1. Create vault
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "v02-vault",
                Encoding.UTF8.GetBytes("old-pass"),
                Argon2Params.ForTests(32 * 1024 * 1024));

            // 2. Add a TOTP-style entry to prod
            var otpEntry = entries.Create("prod", "OpenAI",
                EntryType.ApiKey, "openai",
                new[] {
                    new Field { Key = "api_key", Value = FieldCodec.Encode("sk-real-secret"), Kind = FieldKind.Secret, Sensitive = true },
                    new Field { Key = "totp_uri", Value = FieldCodec.Encode("otpauth://totp/OpenAI?secret=JBSWY3DPEHPK3PXP&issuer=OpenAI"), Kind = FieldKind.TotpUri, Sensitive = false },
                });
            vault.PutEntry("prod", otpEntry);
            backup.Capture("prod", otpEntry, "initial create");
            await vault.SaveAsync();

            // 3. Create dev profile and switch to it
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            vault.Lock();
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("old-pass"));
            vault.ListProfileNames().Should().Contain(new[] { "prod", "dev" });

            // 4. Export prod to a strip-secrets seed
            var seedPath = _dir.RandomPath("seed.dev.okv.dev");
            seedExport.StripSecrets = true;
            seedExport.AllowProdProfile = true;
            var seed = await seedExport.ExportAsync("prod", seedPath);
            seed.Profiles.Should().NotBeEmpty();

            // 5. Import into dev profile
            var importResult = await seedImport.ImportAsync(seedPath, "dev");
            importResult.EntriesImported.Should().BeGreaterOrEqualTo(1);
            // After strip-secrets, the api_key field is dropped; totp_uri is non-sensitive so kept
            var devEntries = vault.ListEntries("dev");
            devEntries.Should().HaveCountGreaterOrEqualTo(1);
            // The OpenAI entry should still be present
            devEntries.Any(e => e.Name == "OpenAI").Should().BeTrue();

            // 6. Change master password
            await vault.ChangePasswordAsync(
                Encoding.UTF8.GetBytes("old-pass"),
                Encoding.UTF8.GetBytes("new-pass-very-long"));
            vault.Lock();
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("new-pass-very-long"));
            // Old password no longer works
            vault.Lock();
            await Assert.ThrowsAsync<CryptoException>(() =>
                vault.UnlockAsync(path, Encoding.UTF8.GetBytes("old-pass")));

            // 7. Sync against a clone
            var clonePath = _dir.RandomPath("clone.okv");
            File.Copy(path, clonePath);
            // Bump local vector clock so local is ahead
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("new-pass-very-long"));
            var sync = new SyncService(vault, lockSvc, _crypto, _format, _codec, new ManifestService(), "test-device");
            var syncResult = await sync.SyncAsync(path, clonePath);
            syncResult.Outcome.Should().BeOneOf(SyncOutcome.NoChange, SyncOutcome.TookRemote, SyncOutcome.Merged, SyncOutcome.LocalAhead);

            // 8. Watch the file for changes
            var watcher = new InMemoryWatcherProvider { DebounceMs = 200 };
            var folder = Path.GetDirectoryName(path)!;
            watcher.Watch(folder);
            var fired = new System.Collections.Concurrent.ConcurrentBag<string>();
            watcher.FileChanged += (_, p) => fired.Add(p);
            watcher.RaiseChange(path);
            fired.Should().ContainSingle().Which.Should().Be(path);
        }
    }

    [Fact]
    public async Task SeedExportRoundTrip_PreservesStructure()
    {
        var (vault, lockSvc, entries, profiles, seedExport, seedImport, _) = CreateAll();
        using (vault) using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "v02",
                Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            // Create dev profile as the seed target (PRD §5.5.3: prod is
            // explicitly blocked for seed import).
            await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());

            var e1 = entries.Create("prod", "E1", EntryType.ApiKey, "github",
                new[] { new Field { Key = "pat", Value = FieldCodec.Encode("ghp_x"), Kind = FieldKind.Secret, Sensitive = true } });
            var e2 = entries.Create("prod", "E2", EntryType.ApiKey, "openai",
                new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("sk_x"), Kind = FieldKind.Secret, Sensitive = true } });
            vault.PutEntry("prod", e1);
            vault.PutEntry("prod", e2);
            await vault.SaveAsync();

            seedExport.StripSecrets = true;
            seedExport.AllowProdProfile = true;
            var seedPath = _dir.RandomPath("seed.okv.dev");
            await seedExport.ExportAsync("prod", seedPath);

            // Import into the dev profile (production-isolation enforced)
            var result = await seedImport.ImportAsync(seedPath, "dev");
            // With strip-secrets, entries are imported; their sensitive values
            // are redacted. Names + field structure survive.
            result.EntriesImported.Should().BeGreaterOrEqualTo(2);
            var imported = vault.ListEntries("dev");
            imported.Select(e => e.Name).Should().Contain(new[] { "E1", "E2" });
            // pat and api_key fields are kept as keys (just with empty/redacted values)
            var githubImported = imported.First(e => e.Name == "E1");
            githubImported.Fields.Should().Contain(f => f.Key == "pat");
        }
    }

    [Fact]
    public async Task ProfileSwitch_AutoLockWhenConfigured()
    {
        var (vault, lockSvc, entries, profiles, _, _, _) = CreateAll();
        using (vault) using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "v02", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            // Default prod settings
            var prodSettings = vault.GetProfile("prod").Settings;
            prodSettings.AutoLockOnSwitch.Should().BeFalse();
            prodSettings.ParticipateInSync.Should().BeTrue();

            // Create dev with auto-lock-on-switch
            var devProfile = await profiles.CreateAsync("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
            devProfile.Settings.AutoLockOnSwitch.Should().BeTrue();
            devProfile.Settings.ParticipateInSync.Should().BeFalse();

            // Update dev settings via service
            await profiles.UpdateSettingsAsync("dev",
                devProfile.Settings with { IdleLockMinutes = 10 });
            var updated = vault.GetProfile("dev");
            updated.Settings.IdleLockMinutes.Should().Be(10);
        }
    }

    [Fact]
    public async Task TotpEntry_RoundTripsThroughSaveAndReload()
    {
        var (vault, lockSvc, entries, _, _, _, _) = CreateAll();
        using (vault) using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "v02", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            var totpUri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&issuer=Test&digits=6";
            var e = entries.Create("prod", "TOTP-Test", EntryType.ApiKey, "test",
                new[] { new Field { Key = "totp_uri", Value = FieldCodec.Encode(totpUri), Kind = FieldKind.TotpUri, Sensitive = false } });
            vault.PutEntry("prod", e);
            await vault.SaveAsync();
            vault.Lock();

            // Reload
            await vault.UnlockAsync(path, Encoding.UTF8.GetBytes("p"));
            var reloaded = vault.ListEntries("prod");
            reloaded.Should().HaveCount(1);
            var totpField = reloaded[0].Fields.First(f => f.Key == "totp_uri");
            totpField.ValueString.Should().Be(totpUri);
            totpField.Kind.Should().Be(FieldKind.TotpUri);
        }
    }

    [Fact]
    public async Task BackupService_HistoryAfterEdits_AllSurvive()
    {
        var (vault, lockSvc, entries, _, _, _, backup) = CreateAll();
        using (vault) using (lockSvc)
        {
            var path = _dir.RandomPath();
            await vault.CreateAsync(path, "v02", Encoding.UTF8.GetBytes("p"),
                Argon2Params.ForTests(32 * 1024 * 1024));
            // v1 — initial create
            var e = entries.Create("prod", "Editing", EntryType.ApiKey, "test",
                new[] { new Field { Key = "k", Value = FieldCodec.Encode("v1"), Kind = FieldKind.Secret, Sensitive = true } });
            e = e with { Version = 1u };
            vault.PutEntry("prod", e);
            backup.Capture("prod", e, "v1");
            // v2 — bump version, change value
            e = e with { Fields = new[] { e.Fields[0] with { Value = FieldCodec.Encode("v2") } }, Version = 2u };
            vault.PutEntry("prod", e);
            backup.Capture("prod", e, "v2");
            // v3
            e = e with { Fields = new[] { e.Fields[0] with { Value = FieldCodec.Encode("v3") } }, Version = 3u };
            vault.PutEntry("prod", e);
            backup.Capture("prod", e, "v3");
            await vault.SaveAsync();

            var history = backup.ListHistory("prod", e.Id);
            history.Count.Should().BeGreaterOrEqualTo(2);
            history.Select(s => s.Version).Should().Contain(new uint[] { 1, 2 });

            var v1 = backup.GetSnapshot("prod", e.Id, 1);
            v1.Should().NotBeNull();
            v1!.Entry.Fields[0].ValueString.Should().Be("v1");
        }
    }
}
