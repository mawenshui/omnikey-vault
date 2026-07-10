﻿﻿﻿using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Result of a sync attempt, per PRD \u00a710.2 / ROADMAP S4-T4.
/// </summary>
public enum SyncOutcome
{
    /// <summary>No remote changes detected; local state unchanged.</summary>
    NoChange,
    /// <summary>Local was ahead of remote; the remote file would be a replay (SEC-T7-01). No action.</summary>
    LocalAhead,
    /// <summary>Local was behind remote; the local state was replaced by the remote state.</summary>
    TookRemote,
    /// <summary>Local and remote were concurrent; entries were merged entry-by-entry. May have conflicts.</summary>
    Merged,
    /// <summary>Sync failed because the remote file is corrupt or unreadable.
    /// Maps to CLI exit code 13 (FileCorrupt) per INTERNAL.md §3.</summary>
    FailedRemoteUnreadable,
    /// <summary>Sync failed because the remote vault has a different UUID than the
    /// local vault — they are different vault instances and cannot be merged.
    /// The user should either re-open with the remote vault file or create an
    /// empty local vault and re-sync to pull the remote.</summary>
    RemoteVaultMismatch,
    /// <summary>Sync failed because of an unrecoverable conflict requiring manual
    /// resolution via the GUI wizard. Maps to CLI exit code 14 (SyncConflict)
    /// per INTERNAL.md §3.</summary>
    FailedConflict
}

public sealed record SyncResult(
    SyncOutcome Outcome,
    Manifest? LocalManifest,
    Manifest? RemoteManifest,
    int EntriesMerged,
    int ConflictsDetected,
    string Message
);

/// <summary>
/// Multi-device sync service per PRD \u00a710 / ROADMAP S4-T4.
///
/// Design (v0.2):
///   - The sync directory holds <c>vault.okv</c> and <c>manifest.json</c> (siblings).
///   - Sync is "pull-based" for v0.2: the local instance reads the remote file,
///     compares vector clocks, and merges (or takes) into the local state.
///   - The user invokes <c>sync force</c> from the CLI; an OS file-system watcher
///     (added in v0.2.1) can trigger auto-sync on remote changes.
///   - All merge rules follow PRD \u00a710.2 \u2014 entry-level version comparison, with
///     <c>local-side wins</c> as the default for same-version-different-content (PRD \u00a74.7).
/// </summary>
[OmniKeyVaultService]
public sealed class SyncService
{
    private readonly VaultService _vault;
    private readonly LockService _lock;
    private readonly ICryptoProvider _crypto;
    private readonly IVaultFormat _format;
    private readonly ProfilePayloadCodec _codec;
    private readonly ManifestService _manifests;
    private readonly string _deviceId;
    // P5-T4: Watcher for auto-sync on remote file changes
    private IWatcherProvider? _watcher;
    private string? _watchedVaultPath;
    private CancellationTokenSource? _debounceCts;
    // §3.2: consecutive sync failure counter — pauses watcher after 5 failures
    private int _consecutiveFailures;
    private const int MaxConsecutiveFailures = 5;

    /// <summary>§3.2: Fired when a sync error occurs, so the GUI can show a toast/dialog.</summary>
    public event Action<string, Exception>? SyncError;

    public SyncService(VaultService vault, LockService lockService, ICryptoProvider crypto,
                      IVaultFormat format, ProfilePayloadCodec codec,
                      ManifestService manifests, string deviceId,
                      IWatcherProvider? watcher = null)
    {
        _vault = vault;
        _lock = lockService;
        _crypto = crypto;
        _format = format;
        _codec = codec;
        _manifests = manifests;
        _deviceId = deviceId;
        _watcher = watcher;
    }

    /// <summary>P5-T4: Start watching the vault's directory for remote changes.
    /// On file change, debounces 200ms then triggers MergeWithRemoteAsync.</summary>
    public Task StartWatchAsync(string vaultFilePath, CancellationToken ct = default)
    {
        if (_watcher == null) return Task.CompletedTask;
        _watchedVaultPath = vaultFilePath;
        var dir = Path.GetDirectoryName(vaultFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Task.CompletedTask;

        _watcher.FileChanged += OnFileChanged;
        _watcher.Watch(dir, "*.okv");
        return Task.CompletedTask;
    }

    /// <summary>P5-T4: Stop watching and release resources.</summary>
    public void StopWatch()
    {
        if (_watcher == null) return;
        _watcher.FileChanged -= OnFileChanged;
        if (_watchedVaultPath != null)
        {
            var dir = Path.GetDirectoryName(_watchedVaultPath);
            if (dir != null) _watcher.Unwatch(dir);
        }
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        _watchedVaultPath = null;
    }

    /// <summary>P5-T5: Debounced file change handler. Multiple events within
    /// 200ms are coalesced into a single MergeWithRemoteAsync call.</summary>
    private async void OnFileChanged(object? sender, string fullPath)
    {
        // Skip if locked or no vault path
        if (!_lock.IsUnlocked || _watchedVaultPath == null) return;
        // Skip if the change is on our own file (we wrote it)
        if (string.Equals(fullPath, _watchedVaultPath, StringComparison.OrdinalIgnoreCase)) return;
        // Only process .okv files
        if (!fullPath.EndsWith(".okv", StringComparison.OrdinalIgnoreCase)) return;

        // Debounce: cancel previous pending merge
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        try
        {
            await Task.Delay(200, ct);
            // Re-check lock state after delay
            if (!_lock.IsUnlocked || _watchedVaultPath == null) return;
            await SyncAsync(_watchedVaultPath, fullPath, ct);
            // §3.2: Reset failure counter on success
            _consecutiveFailures = 0;
        }
        catch (OperationCanceledException) { /* expected on debounce */ }
        catch (Exception ex)
        {
            // §3.2: Proper exception handling instead of silent swallow.
            // Log the error, notify subscribers, and track consecutive failures.
            _consecutiveFailures++;
            System.Diagnostics.Debug.WriteLine($"[SyncService] OnFileChanged error ({_consecutiveFailures}/{MaxConsecutiveFailures}): {ex.GetType().Name}: {ex.Message}");
            SyncError?.Invoke($"同步失败: {ex.Message}", ex);
            // Pause watcher after too many consecutive failures
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncService] Pausing watcher after {MaxConsecutiveFailures} consecutive failures");
                StopWatch();
                SyncError?.Invoke($"连续 {MaxConsecutiveFailures} 次同步失败,已暂停自动同步。请检查网络或文件权限后手动同步。", ex);
            }
        }
    }

    /// <summary>The manifest path is always the sibling of vault.okv named manifest.json.</summary>
    public static string ManifestPathFor(string vaultFilePath)
    {
        var dir = Path.GetDirectoryName(vaultFilePath) ?? ".";
        return Path.Combine(dir, "manifest.json");
    }

    /// <summary>
    /// Reads the current local manifest, building a default one if it does not exist yet.
    /// </summary>
    public async Task<Manifest> GetOrCreateLocalManifestAsync(string vaultFilePath, CancellationToken ct = default)
    {
        var path = ManifestPathFor(vaultFilePath);
        var existing = await _manifests.TryReadAsync(path, ct);
        if (existing != null) return existing;
        return BuildLocalManifest();
    }

    /// <summary>Performs a sync against the given remote file. Returns the outcome + a human-readable message.</summary>
    public async Task<SyncResult> SyncAsync(string vaultFilePath, string remoteFilePath, CancellationToken ct = default)
    {
        _lock.EnsureUnlocked();
        if (!File.Exists(remoteFilePath))
            return new SyncResult(SyncOutcome.NoChange, null, null, 0, 0, $"No remote file at {remoteFilePath}.");
        if (string.Equals(Path.GetFullPath(remoteFilePath), Path.GetFullPath(vaultFilePath), StringComparison.OrdinalIgnoreCase))
            return new SyncResult(SyncOutcome.NoChange, null, null, 0, 0, "Remote and local are the same file. No-op.");

        Manifest? remoteManifest = null;
        try { remoteManifest = await _manifests.TryReadAsync(ManifestPathFor(remoteFilePath), ct); }
        catch { /* missing or corrupt remote manifest: we still try to merge the .okv */ }

        VaultRecord remoteRecord;
        try
        {
            remoteRecord = await _format.ReadAsync(remoteFilePath, ct);
        }
        catch (Exception ex)
        {
            return new SyncResult(SyncOutcome.FailedRemoteUnreadable, null, remoteManifest, 0, 0,
                $"Failed to read remote vault: {ex.Message}");
        }

        // -- UUID check: if the remote vault is a different vault instance,
        // we cannot merge (different KEKs). Handle gracefully. --
        var localUuid = _vault.CurrentVault?.Metadata.Uuid ?? Guid.Empty;
        if (remoteRecord.VaultUuid != localUuid)
        {
            // Check if the local vault is empty (fresh install scenario:
            // user created a vault on this device, then tries to sync with
            // a remote vault from another device).
            bool localIsEmpty = _vault.Profiles.Values.All(p => p.Entries.Count == 0);
            if (localIsEmpty)
            {
                // Safe to replace the empty local vault with the remote.
                var takeResult = await TakeRemoteCoreAsync(vaultFilePath, remoteFilePath, remoteRecord, remoteManifest, ct);
                return takeResult with { Message = "已从云端拉取金库。由于远端金库与本地不同，请锁定后使用远端金库的密码重新解锁。" };
            }
            // Local vault has user data — cannot silently replace.
            return new SyncResult(SyncOutcome.RemoteVaultMismatch,
                await GetOrCreateLocalManifestAsync(vaultFilePath, ct),
                remoteManifest, 0, 0,
                "远端金库与本地金库不匹配（不同金库实例）。如需使用远端金库，请先锁定当前金库，然后新建空白金库后重新同步，或直接打开远端金库文件。");
        }

        var localClock = _vault.CurrentVectorClock;
        var remoteClock = remoteRecord.VectorClock;

        // -- Vector clock comparison (PRD \u00a710.2) --
        var cmp = localClock.Compare(remoteClock);
        if (cmp == 0)
        {
            // Identical clocks \u2014 no changes either way.
            return new SyncResult(SyncOutcome.NoChange, await GetOrCreateLocalManifestAsync(vaultFilePath, ct),
                remoteManifest, 0, 0, "No changes (vector clocks equal).");
        }
        if (cmp == 1)
        {
            // Local is strictly ahead \u2014 remote is a replay (SEC-T7-01). Refuse.
            return new SyncResult(SyncOutcome.LocalAhead, await GetOrCreateLocalManifestAsync(vaultFilePath, ct),
                remoteManifest, 0, 0, "Local is ahead of remote; refusing to accept the older remote state (replay defense).");
        }
        if (cmp == -1)
        {
            // Local is strictly behind — take the remote wholesale.
            return await TakeRemoteCoreAsync(vaultFilePath, remoteFilePath, remoteRecord, remoteManifest, ct);
        }
        // Concurrent: merge entry-by-entry.
        return await MergeWithRemoteAsync(vaultFilePath, remoteFilePath, remoteRecord, remoteManifest, ct);
    }

    /// <summary>
    /// Public entry point for "take remote" (used by GUI conflict resolver).
    /// Backs up the current local vault to <c>vault.okv.pre-sync-{unix}</c> then
    /// copies the remote file over the local one and persists the remote manifest.
    /// </summary>
    public Task<SyncResult> TakeRemoteAsync(string vaultFilePath, string remoteFilePath, CancellationToken ct = default)
    {
        _lock.EnsureUnlocked();
        return TakeRemoteInternalAsync(vaultFilePath, remoteFilePath, null, null, ct);
    }

    /// <summary>Re-applies the concurrent-merge pass without re-reading the remote
    /// record (used by the GUI conflict resolver when the user re-confirms "merge"
    /// after seeing the conflict counts). Equivalent to the initial merge — local
    /// always wins on same-version-different-content per PRD §4.7.</summary>
    public async Task<SyncResult> ApplyLocalWinsMergeAsync(string vaultFilePath, string remoteFilePath, CancellationToken ct = default)
    {
        _lock.EnsureUnlocked();
        Manifest? remoteManifest = null;
        try { remoteManifest = await _manifests.TryReadAsync(ManifestPathFor(remoteFilePath), ct); } catch { }
        VaultRecord remoteRecord;
        try
        {
            remoteRecord = await _format.ReadAsync(remoteFilePath, ct);
        }
        catch (Exception ex)
        {
            return new SyncResult(SyncOutcome.FailedRemoteUnreadable, null, remoteManifest, 0, 0,
                $"Failed to read remote vault: {ex.Message}");
        }
        return await MergeWithRemoteAsync(vaultFilePath, remoteFilePath, remoteRecord, remoteManifest, ct);
    }

    private async Task<SyncResult> TakeRemoteInternalAsync(string vaultFilePath, string remoteFilePath,
        VaultRecord? remoteRecord, Manifest? remoteManifest, CancellationToken ct)
    {
        if (remoteRecord == null)
        {
            try { remoteRecord = await _format.ReadAsync(remoteFilePath, ct); }
            catch (Exception ex)
            {
            return new SyncResult(SyncOutcome.FailedRemoteUnreadable, null, remoteManifest, 0, 0,
                $"Failed to read remote vault: {ex.Message}");
            }
        }
        if (remoteManifest == null)
        {
            try { remoteManifest = await _manifests.TryReadAsync(ManifestPathFor(remoteFilePath), ct); } catch { }
        }
        return await TakeRemoteCoreAsync(vaultFilePath, remoteFilePath, remoteRecord, remoteManifest, ct);
    }

    private async Task<SyncResult> TakeRemoteCoreAsync(string vaultFilePath, string remoteFilePath,
        VaultRecord remoteRecord, Manifest? remoteManifest, CancellationToken ct)
    {
        // Copy the remote .okv to the local path (atomic), then re-derive in-memory state.
        // For v0.2 simplicity we do this by re-locking and re-unlocking; the user will
        // not see this in the CLI flow because we keep the unlock state.
        _lock.EnsureUnlocked();
        // 1. Backup local file (so a TakeRemote that later turns out wrong can be undone).
        var backup = vaultFilePath + ".pre-sync-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (File.Exists(vaultFilePath)) File.Copy(vaultFilePath, backup, overwrite: true);
        try
        {
            File.Copy(remoteFilePath, vaultFilePath, overwrite: true);
            // Persist remote manifest (if any) to local manifest path.
            if (remoteManifest != null)
            {
                var merged = WithRemoteClock(remoteManifest, remoteRecord.VectorClock);
                await _manifests.WriteAsync(ManifestPathFor(vaultFilePath), merged, ct);
            }
            else
            {
                await _manifests.WriteAsync(ManifestPathFor(vaultFilePath), BuildLocalManifest(remoteRecord), ct);
            }
            return new SyncResult(SyncOutcome.TookRemote, await GetOrCreateLocalManifestAsync(vaultFilePath, ct),
                remoteManifest, 0, 0, $"Took remote state from {remoteFilePath} (backup: {backup}).");
        }
        catch
        {
            // Restore backup on failure.
            if (File.Exists(backup)) File.Copy(backup, vaultFilePath, overwrite: true);
            throw;
        }
    }

    private async Task<SyncResult> MergeWithRemoteAsync(string vaultFilePath, string remoteFilePath,
        VaultRecord remoteRecord, Manifest? remoteManifest, CancellationToken ct)
    {
        // For v0.2, merge is per-profile at the entry level:
        //   - For each profile present in both local and remote, compare each entry.
        //   - If entry version differs: keep the higher version.
        //   - If version is equal but content differs: local-side wins (PRD \u00a74.7),
        //     count as a conflict.
        //   - New entries on either side are added.
        //
        // We can't directly compare the two VaultRecord objects' DEK material (it's
        // wrapped). Instead, we work with the in-memory decrypted profiles for the
        // local side and the (decrypted) payload for the remote side.
        int entriesMerged = 0;
        int conflicts = 0;

        // Decrypt remote profiles (we have the same KEK since both vaults share
        // the same master password + same per-vault salt).
        var remoteProfiles = await DecryptRemoteProfilesAsync(remoteRecord, ct);

        foreach (var remoteProfile in remoteProfiles)
        {
            if (!_vault.Profiles.TryGetValue(remoteProfile.Name, out var localProfile))
            {
                // Remote profile doesn't exist locally \u2014 add it.
                _vault.CreateProfile(remoteProfile.Name, remoteProfile.Color, remoteProfile.Settings);
                _vault.GetDekForSeed(remoteProfile.Name);  // ensure DEK cached
            }
            localProfile = _vault.GetProfile(remoteProfile.Name);
            var localById = localProfile.Entries.ToDictionary(e => e.Id, e => e, GuidEqualityComparer.Instance);

            foreach (var remoteEntry in remoteProfile.Entries)
            {
                if (!localById.TryGetValue(remoteEntry.Id, out var localEntry))
                {
                    // New entry \u2014 import.
                    _vault.PutEntry(remoteProfile.Name, remoteEntry);
                    entriesMerged++;
                }
                else if (remoteEntry.Version > localEntry.Version)
                {
                    // Remote is newer \u2014 overwrite local.
                    _vault.PutEntry(remoteProfile.Name, remoteEntry);
                    entriesMerged++;
                }
                else if (remoteEntry.Version == localEntry.Version && !EntriesEqual(localEntry, remoteEntry))
                {
                    // Same version, different content: local-side wins (PRD \u00a74.7).
                    conflicts++;
                }
                // else: local is newer or equal-and-same \u2014 no-op.
            }
        }

        // Persist merged state to local.
        await _vault.SaveAsync(ct);

        // Update local manifest with the merged vector clock.
        var mergedClock = _vault.CurrentVectorClock.Merge(remoteRecord.VectorClock);
        var localManifest = (await GetOrCreateLocalManifestAsync(vaultFilePath, ct)) with
        {
            VectorClock = mergedClock,
            LastModified = DateTimeOffset.UtcNow,
            LastModifiedBy = _deviceId,
            Profiles = _vault.ListProfileNames().ToList()
        };
        await _manifests.WriteAsync(ManifestPathFor(vaultFilePath), localManifest, ct);
        return new SyncResult(SyncOutcome.Merged, localManifest, remoteManifest, entriesMerged, conflicts,
            $"Merged {entriesMerged} new/updated entries; {conflicts} same-version-different-content conflicts (local-wins).");
    }

    /// <summary>Builds a manifest from the current in-memory vault state.</summary>
    public Manifest BuildLocalManifest(VaultRecord? record = null)
    {
        _lock.EnsureUnlocked();
        // When a remote record is provided (e.g. TakeRemote from a different vault),
        // use its UUID and vector clock instead of the stale local vault state.
        var vc = record?.VectorClock ?? _vault.CurrentVectorClock;
        var uuid = record?.VaultUuid ?? _vault.CurrentVault?.Metadata.Uuid ?? Guid.Empty;
        var profiles = record != null
            ? record.Profiles.Select(p => p.Name).ToList()
            : _vault.ListProfileNames().ToList();
        var pubKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var localPk = _vault.DevicePublicKey;
        if (localPk != null) pubKeys[_deviceId] = Convert.ToBase64String(localPk.Bytes);
        return new Manifest
        {
            VaultUuid = uuid,
            DeviceId = _deviceId,
            LastModified = DateTimeOffset.UtcNow,
            LastModifiedBy = _deviceId,
            Profiles = profiles,
            VectorClock = vc,
            SchemaVersion = 1,
            OkvFormatVersion = "1.0",
            DevicePublicKeys = pubKeys
        };
    }

    private async Task<List<Profile>> DecryptRemoteProfilesAsync(VaultRecord remoteRecord, CancellationToken ct)
    {
        // Both vaults share the same KEK (derived from the same master password + salt).
        // We use the in-memory KEK to unwrap each remote profile's DEK and decrypt its payload.
        var kek = _lock.CurrentKek ?? throw new VaultLockedException("KEK not available.");
        var profiles = new List<Profile>();
        foreach (var pr in remoteRecord.Profiles)
        {
            using var dek = _crypto.UnwrapKey(kek, pr.WrappedDek);
            var payload = new EncryptedPayload(
                pr.PayloadNonce, pr.EncryptedPayload, pr.PayloadTag,
                VaultCryptoHelpers.BuildProfileAad(remoteRecord.VaultUuid, pr.Id));
            var body = _crypto.Decrypt(dek, in payload, VaultCryptoHelpers.BuildProfileAad(remoteRecord.VaultUuid, pr.Id));
            var (entries, folders, tags, templates) = _codec.Decode(body);
            CryptographicOperations.ZeroMemory(body);
            // Cache the DEK in the local LockService so the merge can PutEntry.
            _lock.CacheDek(pr.Name, DataEncryptionKey.From(dek.Span.ToArray()));
            profiles.Add(new Profile
            {
                Id = pr.Id,
                Name = pr.Name,
                Color = pr.Color,
                Settings = pr.Settings,
                Entries = entries,
                Folders = folders,
                Templates = templates
            });
        }
        await Task.CompletedTask;
        return profiles;
    }


    private static Manifest WithRemoteClock(Manifest m, VectorClock remoteClock)
        => m with { VectorClock = m.VectorClock.Merge(remoteClock) };

    private static bool EntriesEqual(Entry a, Entry b)
    {
        if (a.Id != b.Id) return false;
        if (a.Name != b.Name) return false;
        if (a.Notes != b.Notes) return false;
        if (a.ExpiresAt != b.ExpiresAt) return false;
        if (!a.Tags.SequenceEqual(b.Tags)) return false;
        if (a.Fields.Count != b.Fields.Count) return false;
        for (int i = 0; i < a.Fields.Count; i++)
        {
            var fa = a.Fields[i];
            var fb = b.Fields[i];
            if (fa.Key != fb.Key || !fa.Value.AsSpan().SequenceEqual(fb.Value)) return false;
        }
        return true;
    }

    /// <summary>Compares two Guid values using OrdinalIgnoreCase (matches .NET Guid.Equals).</summary>
    private sealed class GuidEqualityComparer : IEqualityComparer<Guid>
    {
        public static readonly GuidEqualityComparer Instance = new();
        public bool Equals(Guid x, Guid y) => x.Equals(y);
        public int GetHashCode(Guid g) => g.GetHashCode();
    }
}
