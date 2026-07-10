using System.Text;
using System.Text.Json;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Sync;

/// <summary>
/// Tests for ManifestService (manifest.json read/write) per OKV_FORMAT.md §8.
/// </summary>
public class ManifestServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly ManifestService _svc = new();

    public ManifestServiceTests(TempVaultDir dir) { _dir = dir; }

    private Manifest MakeManifest() => new()
    {
        VaultUuid = Guid.NewGuid(),
        DeviceId = "laptop-abc",
        LastModified = DateTimeOffset.UtcNow,
        LastModifiedBy = "laptop-abc",
        Profiles = new[] { "prod", "dev" },
        VectorClock = new VectorClock(new Dictionary<string, long> { ["laptop-abc"] = 5, ["workstation-def"] = 3 }),
        SchemaVersion = 1,
        OkvFormatVersion = "1.0",
        DevicePublicKeys = new Dictionary<string, string> { ["laptop-abc"] = "AAAA", ["workstation-def"] = "BBBB" }
    };

    [Fact]
    public async Task WriteThenRead_Roundtrip_PreservesAllFields()
    {
        var path = _dir.RandomPath("json");
        var m = MakeManifest();
        await _svc.WriteAsync(path, m);
        var read = await _svc.ReadAsync(path);
        read.VaultUuid.Should().Be(m.VaultUuid);
        read.DeviceId.Should().Be("laptop-abc");
        read.Profiles.Should().Equal("prod", "dev");
        read.VectorClock.Get("laptop-abc").Should().Be(5);
        read.VectorClock.Get("workstation-def").Should().Be(3);
        read.DevicePublicKeys["laptop-abc"].Should().Be("AAAA");
    }

    [Fact]
    public async Task Write_Atomic_NoTempFileLeft()
    {
        var path = _dir.RandomPath("json");
        await _svc.WriteAsync(path, MakeManifest());
        File.Exists(path + ".tmp").Should().BeFalse();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Write_OverwritesExisting()
    {
        var path = _dir.RandomPath("json");
        var m1 = MakeManifest();
        await _svc.WriteAsync(path, m1);
        var m2 = m1 with { DeviceId = "device-2" };
        await _svc.WriteAsync(path, m2);
        var read = await _svc.ReadAsync(path);
        read.DeviceId.Should().Be("device-2");
    }

    [Fact]
    public async Task Read_MissingFile_Throws()
    {
        var act = async () => await _svc.ReadAsync(_dir.RandomPath("json"));
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task TryRead_MissingFile_ReturnsNull()
    {
        var result = await _svc.TryReadAsync(_dir.RandomPath("json"));
        result.Should().BeNull();
    }

    [Fact]
    public async Task Read_MalformedJson_Throws()
    {
        var path = _dir.RandomPath("json");
        await File.WriteAllTextAsync(path, "{ not valid json ");
        var act = async () => await _svc.ReadAsync(path);
        await act.Should().ThrowAsync<JsonException>();
    }
}

/// <summary>
/// Two-instance sync tests (PRD §10.2 / ROADMAP S4-T4 / S4-T9).
/// Exercises the merge algorithm end-to-end with two vault.okv files sharing
/// the same master password (the standard sync model per PRD §5.4).
/// </summary>
public class SyncServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;
    private readonly SodiumCryptoProvider _crypto = new();
    private readonly VaultFormat _vaultFmt = new();
    private readonly ProfilePayloadCodec _codec = new();
    private readonly ManifestService _manifests = new();
    private const string Password = "shared-pw";
    private const string DeviceA = "device-A";
    private const string DeviceB = "device-B";

    public SyncServiceTests(TempVaultDir dir) { _dir = dir; }

    private async Task<(VaultService vs, LockService ls, string path)> CreateVaultAsync(string deviceId, string profileEntries = "")
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _vaultFmt, ls, _codec, deviceId, new DeviceKeystore());
        var path = _dir.RandomPath();
        await vs.CreateAsync(path, "T", Encoding.UTF8.GetBytes(Password),
            Argon2Params.ForTests(32 * 1024 * 1024));
        return (vs, ls, path);
    }

    [Fact]
    public async Task Sync_NoRemote_ReturnsNoChange()
    {
        var (vs, ls, path) = await CreateVaultAsync(DeviceA);
        using (vs) using (ls)
        {
            var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
            var r = await sync.SyncAsync(path, _dir.RandomPath());
            r.Outcome.Should().Be(SyncOutcome.NoChange);
        }
    }

    [Fact]
    public async Task Sync_SameFile_ReturnsNoChange()
    {
        var (vs, ls, path) = await CreateVaultAsync(DeviceA);
        using (vs) using (ls)
        {
            var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
            var r = await sync.SyncAsync(path, path);
            r.Outcome.Should().Be(SyncOutcome.NoChange);
        }
    }

    [Fact]
    public async Task Sync_IdenticalClocks_ReturnsNoChange()
    {
        // The "remote" is a copy of the local file. Vector clocks are equal.
        var pathA = _dir.RandomPath();
        await BuildAndLockAsync(pathA, DeviceA);
        var pathB = _dir.RandomPath();
        File.Copy(pathA, pathB, overwrite: true);
        var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
        using (vs) using (ls)
        {
            var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
            var r = await sync.SyncAsync(pathA, pathB);
            r.Outcome.Should().Be(SyncOutcome.NoChange);
        }
    }

    [Fact]
    public async Task Sync_LocalBehindRemote_TakesRemote()
    {
        // The "remote" was written twice by the same user; local was written once.
        // Simulate device B with extra writes (on the same vault by copying).
        var pathA = _dir.RandomPath();
        var pathB = _dir.RandomPath();
        await BuildAndLockAsync(pathA, DeviceA, extraWrites: 0);
        File.Copy(pathA, pathB, overwrite: true);
        // B-side: open pathB, write once more.
        {
            var (vs, ls, _) = await OpenAsync(pathB, DeviceB);
            using (vs) using (ls)
            {
                vs.PutEntry("prod", MakeEntry("from-B", DeviceB));
                await vs.SaveAsync();
            }
        }
        // A-side: sync from B.
        var (vsA, lsA, _) = await OpenAsync(pathA, DeviceA);
        using (vsA) using (lsA)
        {
            var sync = new SyncService(vsA, lsA, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
            var r = await sync.SyncAsync(pathA, pathB);
            r.Outcome.Should().Be(SyncOutcome.TookRemote);
        }
    }

    [Fact]
    public async Task Sync_LocalAheadOfRemote_RejectsReplay()
    {
        // SEC-T7-01: replay defense — local must NOT accept a remote file with an
        // older vector clock.
        var pathA = _dir.RandomPath();
        var pathB = _dir.RandomPath();
        // Both start from the same baseline.
        await BuildAndLockAsync(pathA, DeviceA);
        File.Copy(pathA, pathB, overwrite: true);
        // A writes one more entry, advancing A's clock.
        {
            var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
            using (vs) using (ls)
            {
                vs.PutEntry("prod", MakeEntry("from-A", DeviceA));
                await vs.SaveAsync();
            }
        }
        // B still has the baseline. A is ahead.
        var (vsA, lsA, _) = await OpenAsync(pathA, DeviceA);
        using (vsA) using (lsA)
        {
            var sync = new SyncService(vsA, lsA, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
            var r = await sync.SyncAsync(pathA, pathB);
            r.Outcome.Should().Be(SyncOutcome.LocalAhead);
        }
    }

    [Fact]
    public async Task Sync_ConcurrentClocks_MergesEntries()
    {
        // Each side writes a distinct new entry concurrently. Vector clocks end up
        // concurrent because A and B each have a non-zero counter on their own
        // device, and the other device's counter is unchanged.
        var pathA = _dir.RandomPath();
        var pathB = _dir.RandomPath();
        // Start with a baseline that both A and B will sync from. To get a
        // concurrent scenario: open with B, write one entry (B's clock advances),
        // then A also writes an entry (A's clock advances) — now clocks are concurrent.
        await BuildAndLockAsync(pathA, DeviceA);
        File.Copy(pathA, pathB, overwrite: true);

        // B-side: open pathB, write "from-B". Clock = {DeviceA: 1, DeviceB: 1}.
        {
            var (vs, ls, _) = await OpenAsync(pathB, DeviceB);
            using (vs) using (ls)
            {
                vs.PutEntry("prod", MakeEntry("from-B", DeviceB));
                await vs.SaveAsync();
            }
        }
        // A-side: open pathA, write "from-A". Clock = {DeviceA: 2, DeviceB: 0}.
        {
            var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
            using (vs) using (ls)
            {
                vs.PutEntry("prod", MakeEntry("from-A", DeviceA));
                await vs.SaveAsync();
            }
        }
        // Now: A's local clock = {DeviceA: 2}, B's remote clock = {DeviceA: 1, DeviceB: 1}.
        // Compare: A's DeviceA=2 > B's=1, A's DeviceB=0 < B's=1 → concurrent.
        {
            var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
            using (vs) using (ls)
            {
                var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
                var r = await sync.SyncAsync(pathA, pathB);
                r.Outcome.Should().Be(SyncOutcome.Merged);
                // from-B is new from A's view; from-A is already local.
                r.EntriesMerged.Should().BeGreaterOrEqualTo(1);
                // Re-open to see the merged state.
                var vs2 = new VaultService(_crypto, _vaultFmt, new LockService(_crypto), _codec, DeviceA, new DeviceKeystore());
                await vs2.UnlockAsync(pathA, Encoding.UTF8.GetBytes(Password));
                var names = vs2.ListEntries("prod").Select(e => e.Name).ToList();
                names.Should().Contain("from-A");
                names.Should().Contain("from-B");
            }
        }
    }

    [Fact]
    public async Task Sync_ConcurrentClocks_SameVersionDifferentContent_RecordsConflict_LocalWins()
    {
        // Both sides edit the SAME entry concurrently (same version, different content).
        // Per PRD §4.7, local-side wins; conflict count > 0.
        var sharedEntryId = Guid.NewGuid();
        var pathA = _dir.RandomPath();
        var pathB = _dir.RandomPath();
        await BuildAndLockAsync(pathA, DeviceA, preSeedEntry: sharedEntryId);
        File.Copy(pathA, pathB, overwrite: true);

        // A edits: "from-A"
        {
            var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
            using (vs) using (ls)
            {
                var existing = vs.ListEntries("prod").First(e => e.Id == sharedEntryId);
                var edited = existing with { Name = "from-A", Version = existing.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
                vs.PutEntry("prod", edited);
                await vs.SaveAsync();
            }
        }
        // B edits: "from-B" (concurrent).
        {
            var (vs, ls, _) = await OpenAsync(pathB, DeviceB);
            using (vs) using (ls)
            {
                var existing = vs.ListEntries("prod").First(e => e.Id == sharedEntryId);
                var edited = existing with { Name = "from-B", Version = existing.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
                vs.PutEntry("prod", edited);
                await vs.SaveAsync();
            }
        }
        // A syncs from B.
        {
            var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
            using (vs) using (ls)
            {
                var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
                var r = await sync.SyncAsync(pathA, pathB);
                r.Outcome.Should().Be(SyncOutcome.Merged);
                r.ConflictsDetected.Should().BeGreaterOrEqualTo(1);
                // Local-side wins: A's edit survives.
                var entry = vs.ListEntries("prod").First(e => e.Id == sharedEntryId);
                entry.Name.Should().Be("from-A");
            }
        }
    }

    [Fact]
    public async Task Sync_RemoteHasNewProfile_TakesRemote()
    {
        // The remote (pathB) has a "dev" profile that pathA doesn't. Since
        // vector clocks are not concurrent (B is ahead), the result is TookRemote.
        var pathA = _dir.RandomPath();
        var pathB = _dir.RandomPath();
        await BuildAndLockAsync(pathA, DeviceA);
        File.Copy(pathA, pathB, overwrite: true);
        // B adds a dev profile and saves.
        {
            var (vs, ls, _) = await OpenAsync(pathB, DeviceB);
            using (vs) using (ls)
            {
                vs.CreateProfile("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
                vs.PutEntry("dev", MakeEntry("dev-entry", DeviceB));
                await vs.SaveAsync();
            }
        }
        // A syncs from B.
        {
            var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
            using (vs) using (ls)
            {
                var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
                var r = await sync.SyncAsync(pathA, pathB);
                r.Outcome.Should().Be(SyncOutcome.TookRemote);
                // After TakeRemote, the local file is replaced. Re-unlock to verify.
                var vs2 = new VaultService(_crypto, _vaultFmt, new LockService(_crypto), _codec, DeviceA, new DeviceKeystore());
                await vs2.UnlockAsync(pathA, Encoding.UTF8.GetBytes(Password));
                vs2.Profiles.Should().ContainKey("dev");
            }
        }
    }

    [Fact]
    public async Task Sync_RemoteFileCorrupt_ReturnsFailedRemoteUnreadable()
    {
        var pathA = _dir.RandomPath();
        await BuildAndLockAsync(pathA, DeviceA);
        // Write garbage to a "remote" file.
        var pathB = _dir.RandomPath();
        await File.WriteAllBytesAsync(pathB, new byte[] { 0x01, 0x02, 0x03 });

        var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
        using (vs) using (ls)
        {
            var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
            var r = await sync.SyncAsync(pathA, pathB);
            r.Outcome.Should().Be(SyncOutcome.FailedRemoteUnreadable);
        }
    }

    [Fact]
    public async Task GetOrCreateLocalManifest_BuildsDefaultIfMissing()
    {
        var pathA = _dir.RandomPath();
        await BuildAndLockAsync(pathA, DeviceA);
        var (vs, ls, _) = await OpenAsync(pathA, DeviceA);
        using (vs) using (ls)
        {
            var sync = new SyncService(vs, ls, _crypto, _vaultFmt, _codec, _manifests, DeviceA);
            var m = await sync.GetOrCreateLocalManifestAsync(pathA);
            m.Profiles.Should().Contain("prod");
        }
    }

    // ---- helpers ----

    private static Entry MakeEntry(string name, string deviceId) => new()
    {
        Id = Guid.NewGuid(),
        Type = EntryType.ApiKey,
        Name = name,
        Fields = new[] { new Field { Key = "k", Value = FieldCodec.Encode("v-" + deviceId), Kind = FieldKind.Secret, Sensitive = true } },
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };

    private async Task BuildAndLockAsync(string path, string deviceId, int extraWrites = 0,
        bool addDevProfile = false, Guid? preSeedEntry = null)
    {
        // Create the vault first (so the file exists), then optionally enrich.
        var (vs, ls, _) = await CreateVaultOnDiskAsync(path, deviceId);
        using (vs) using (ls)
        {
            if (addDevProfile)
            {
                vs.CreateProfile("dev", ProfileColor.Yellow, ProfileSettings.DefaultDev());
                vs.PutEntry("dev", MakeEntry("dev-entry", deviceId));
            }
            if (preSeedEntry.HasValue)
            {
                vs.PutEntry("prod", new Entry
                {
                    Id = preSeedEntry.Value,
                    Type = EntryType.ApiKey,
                    Name = "shared",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Version = 1
                });
            }
            for (int i = 0; i < extraWrites; i++)
            {
                vs.PutEntry("prod", MakeEntry("extra-" + i, deviceId));
                await vs.SaveAsync();
            }
            await vs.SaveAsync();
        }
    }

    private async Task<(VaultService vs, LockService ls, string path)> CreateVaultOnDiskAsync(string path, string deviceId)
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _vaultFmt, ls, _codec, deviceId, new DeviceKeystore());
        await vs.CreateAsync(path, "T", Encoding.UTF8.GetBytes(Password),
            Argon2Params.ForTests(32 * 1024 * 1024));
        return (vs, ls, path);
    }

    private async Task<(VaultService vs, LockService ls, string path)> OpenAsync(string path, string deviceId)
    {
        var ls = new LockService(_crypto);
        var vs = new VaultService(_crypto, _vaultFmt, ls, _codec, deviceId, new DeviceKeystore());
        await vs.UnlockAsync(path, Encoding.UTF8.GetBytes(Password));
        return (vs, ls, path);
    }
}
