using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Backup;

/// <summary>
/// Tests for the BackupService (PRD §5.5.2 / ROADMAP S3-T5).
/// Covers: snapshot capture, history listing, restore, and retention.
/// </summary>
public class BackupServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _format = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly DeviceKeystore _keystore = new();

    public BackupServiceTests(TempVaultDir dir) { _dir = dir; }

    private (VaultService vault, LockService lockSvc, BackupService backup, EntryService entries) CreateService(int maxSnaps = 5)
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _format, ls, _codec, "test-device", _keystore);
        var backup = new BackupService(vs, "test-device", maxSnaps);
        var clip = new ClipboardService(new InMemoryClipboardProvider(), ls);
        var entrySvc = new EntryService(vs, new TemplateService(), clip, _crypto);
        return (vs, ls, backup, entrySvc);
    }

    private async Task<string> CreateVaultAsync(VaultService vs)
    {
        var path = _dir.RandomPath();
        await vs.CreateAsync(path, "T", Encoding.UTF8.GetBytes("p"),
            Argon2Params.ForTests(32 * 1024 * 1024));
        return path;
    }

    private static Entry MakeEntry(string name, uint version = 1) => new()
    {
        Id = Guid.NewGuid(),
        Type = EntryType.ApiKey,
        Name = name,
        Fields = new[] { new Field { Key = "k", Value = FieldCodec.Encode("v" + version), Kind = FieldKind.Secret, Sensitive = true } },
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = version
    };

    [Fact]
    public async Task Capture_StoresSnapshot()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var e = MakeEntry("test");
            vault.PutEntry("prod", e);
            var snap = backup.Capture("prod", e, reason: "initial save");
            snap.EntryId.Should().Be(e.Id);
            snap.Version.Should().Be(1);
            snap.Reason.Should().Be("initial save");
            snap.DeviceId.Should().Be("test-device");
        }
    }

    [Fact]
    public async Task ListHistory_ReturnsSnapshotsNewestFirst()
    {
        var (vault, lockSvc, backup, entries) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var e = MakeEntry("e", version: 1);
            vault.PutEntry("prod", e);
            backup.Capture("prod", e, "v1");

            var v2 = e with { Version = 2, Name = "e2" };
            vault.PutEntry("prod", v2);
            backup.Capture("prod", v2, "v2");

            var v3 = e with { Version = 3, Name = "e3" };
            vault.PutEntry("prod", v3);
            backup.Capture("prod", v3, "v3");

            var history = backup.ListHistory("prod", e.Id);
            history.Count.Should().Be(3);
            history[0].Version.Should().Be(3);
            history[1].Version.Should().Be(2);
            history[2].Version.Should().Be(1);
        }
    }

    [Fact]
    public async Task Capture_DeduplicatesByVersion()
    {
        // If a snapshot at the same version is captured twice, the second
        // capture replaces the first (e.g., idempotent re-save).
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var e = MakeEntry("e", version: 1);
            backup.Capture("prod", e, "first");
            backup.Capture("prod", e, "second");
            backup.ListHistory("prod", e.Id).Count.Should().Be(1);
        }
    }

    [Fact]
    public async Task Capture_RespectsRetentionLimit()
    {
        var (vault, lockSvc, backup, _) = CreateService(maxSnaps: 3);
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var e = MakeEntry("e", version: 1);
            for (uint v = 1; v <= 5; v++)
            {
                var ev = e with { Version = v };
                backup.Capture("prod", ev, $"v{v}");
            }
            var history = backup.ListHistory("prod", e.Id);
            history.Count.Should().Be(3);  // bounded by retention
            history.Select(h => h.Version).Should().Equal(new uint[] { 5, 4, 3 });
        }
    }

    [Fact]
    public async Task GetSnapshot_ByVersion_ReturnsCorrectSnapshot()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var e1 = MakeEntry("e", version: 1);
            var e2 = e1 with { Version = 2 };
            backup.Capture("prod", e1, "v1");
            backup.Capture("prod", e2, "v2");

            var snap = backup.GetSnapshot("prod", e1.Id, 1);
            snap.Should().NotBeNull();
            snap!.Reason.Should().Be("v1");
        }
    }

    [Fact]
    public async Task GetSnapshot_UnknownVersion_ReturnsNull()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            backup.GetSnapshot("prod", Guid.NewGuid(), 99).Should().BeNull();
        }
    }

    [Fact]
    public async Task Restore_ToOldVersion_RevertsEntry()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var e1 = MakeEntry("e", version: 1);
            e1.Fields.ElementAt(0).ValueString.Should().Be("v1");
            vault.PutEntry("prod", e1);
            backup.Capture("prod", e1, "v1");

            var e2 = e1 with { Version = 2, Fields = new[] { new Field { Key = "k", Value = FieldCodec.Encode("v2"), Kind = FieldKind.Secret, Sensitive = true } } };
            vault.PutEntry("prod", e2);
            backup.Capture("prod", e2, "v2");

            var e3 = e1 with { Version = 3, Fields = new[] { new Field { Key = "k", Value = FieldCodec.Encode("v3"), Kind = FieldKind.Secret, Sensitive = true } } };
            vault.PutEntry("prod", e3);
            backup.Capture("prod", e3, "v3");

            // Restore to v1
            var restored = backup.Restore("prod", e1.Id, version: 1);
            restored.Version.Should().Be(2);  // v1 + 1
            restored.Fields[0].ValueString.Should().Be("v1");
            restored.Name.Should().Be("e");
        }
    }

    [Fact]
    public async Task Restore_CapturesThePreRestoreState()
    {
        // Restoring should also add the pre-restore state to history (so user can undo).
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var e1 = MakeEntry("e", version: 1);
            vault.PutEntry("prod", e1);
            backup.Capture("prod", e1, "v1");
            var e2 = e1 with { Version = 2, Name = "e2" };
            vault.PutEntry("prod", e2);
            backup.Capture("prod", e2, "v2");

            backup.Restore("prod", e1.Id, version: 1);
            var history = backup.ListHistory("prod", e1.Id);
            // The pre-restore v2 should be captured so the user can undo the restore.
            history.Should().Contain(s => s.Version == 2 && s.Entry.Name == "e2" && s.Reason!.Contains("restore"));
        }
    }

    [Fact]
    public async Task Restore_UnknownEntry_Throws()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var act = () => backup.Restore("prod", Guid.NewGuid(), 1);
            act.Should().Throw<EntryNotFoundException>();
        }
    }

    [Fact]
    public async Task PurgeProfile_RemovesAllSnapshots()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            backup.Capture("prod", MakeEntry("a"), "x");
            backup.Capture("prod", MakeEntry("b"), "y");
            backup.PurgeProfile("prod");
            backup.ListHistory("prod", Guid.NewGuid()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task PurgeEntry_RemovesOnlyThatEntry()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            await CreateVaultAsync(vault);
            var a = MakeEntry("a");
            var b = MakeEntry("b");
            backup.Capture("prod", a, "ax");
            backup.Capture("prod", b, "bx");
            backup.PurgeEntry("prod", a.Id);
            backup.ListHistory("prod", a.Id).Should().BeEmpty();
            backup.ListHistory("prod", b.Id).Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task Capture_WhenLocked_Throws()
    {
        var (vault, lockSvc, backup, _) = CreateService();
        using (vault) using (lockSvc)
        {
            // No create call -> vault is locked.
            var act = () => backup.Capture("prod", MakeEntry("e"), "x");
            act.Should().Throw<VaultLockedException>();
        }
    }
}
