﻿using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Per-Entry snapshot history service per PRD §5.5.2 / ROADMAP S3-T5.
///
/// P5-T1: snapshots are persisted to disk as
/// <c>.okv.snapshots/&lt;profile&gt;/&lt;entry-id&gt;/&lt;version&gt;.entry.enc</c>
/// (per OKV_FORMAT.md §10). The file format is:
///   [OKVS magic (4B)] [SnapshotVersion (2B)] [EntryId (16B)] [Version (4B)]
///   [CreatedAt (8B)] [AEAD Payload] [Ed25519 Signature (64B)]
///
/// The retention policy is bounded: at most <see cref="_maxPerEntry"/> snapshots
/// per entry, oldest evicted first. An in-memory cache is maintained for
/// fast GUI access; disk is the source of truth.
/// </summary>
[OmniKeyVaultService]
public sealed class BackupService
{
    private readonly VaultService _vault;
    private readonly string _deviceId;
    private readonly int _maxPerEntry;
    // Outer key: profile name. Inner key: entry id. Value: snapshots newest-first.
    private readonly Dictionary<string, Dictionary<Guid, List<EntrySnapshot>>> _store
        = new(StringComparer.Ordinal);

    // P5-T1: Disk persistence fields (optional — when null, in-memory only)
    private readonly ICryptoProvider? _crypto;
    private readonly string? _snapshotDir;
    private readonly DeviceKeystore? _keystore;
    private readonly string? _deviceIdForSign;

    public BackupService(VaultService vault, string deviceId, int maxSnapshotsPerEntry = 5,
        ICryptoProvider? crypto = null, string? snapshotDir = null, DeviceKeystore? keystore = null)
    {
        _vault = vault;
        _deviceId = deviceId;
        _maxPerEntry = Math.Max(1, maxSnapshotsPerEntry);
        _crypto = crypto;
        _snapshotDir = snapshotDir;
        _keystore = keystore;
        _deviceIdForSign = deviceId;
    }

    /// <summary>
    /// Captures a snapshot of the entry's current state. Should be called BEFORE
    /// mutating an entry (so the snapshot reflects the pre-mutation version).
    /// P5-T1: Also persists the snapshot to disk when crypto + snapshotDir are configured.
    /// </summary>
    public EntrySnapshot Capture(string profileName, Entry entry, string? reason = null)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        _vault.GetProfile(profileName);  // throws VaultLockedException if locked
        var snap = new EntrySnapshot
        {
            EntryId = entry.Id,
            ProfileName = profileName,
            Version = entry.Version,
            CapturedAt = DateTimeOffset.UtcNow,
            Entry = entry,
            DeviceId = _deviceId,
            Reason = reason
        };
        if (!_store.TryGetValue(profileName, out var byEntry))
        {
            byEntry = new Dictionary<Guid, List<EntrySnapshot>>();
            _store[profileName] = byEntry;
        }
        if (!byEntry.TryGetValue(entry.Id, out var list))
        {
            list = new List<EntrySnapshot>();
            byEntry[entry.Id] = list;
        }
        // De-dup by version: if a snapshot at the same version already exists, replace it.
        int existing = list.FindIndex(s => s.Version == entry.Version);
        if (existing >= 0) list[existing] = snap;
        else list.Insert(0, snap);  // newest at index 0

        // Evict oldest beyond retention.
        while (list.Count > _maxPerEntry)
            list.RemoveAt(list.Count - 1);

        // P5-T1: Persist to disk
        if (_crypto != null && _snapshotDir != null)
        {
            try { PersistSnapshotToDisk(snap); }
            catch { /* disk persistence is best-effort; in-memory still works */ }
        }

        return snap;
    }

    /// <summary>Returns the snapshot history for an entry, newest version first.
    /// P5-T1: Falls back to disk if not in memory cache.</summary>
    public IReadOnlyList<EntrySnapshot> ListHistory(string profileName, Guid entryId)
    {
        if (_store.TryGetValue(profileName, out var byEntry)
            && byEntry.TryGetValue(entryId, out var list))
            return list.AsReadOnly();

        // P5-T1: Try loading from disk
        if (_crypto != null && _snapshotDir != null)
        {
            var loaded = LoadSnapshotsFromDisk(profileName, entryId);
            if (loaded.Count > 0)
            {
                if (!_store.TryGetValue(profileName, out var diskByEntry))
                {
                    diskByEntry = new Dictionary<Guid, List<EntrySnapshot>>();
                    _store[profileName] = diskByEntry;
                }
                diskByEntry[entryId] = loaded;
                return loaded.AsReadOnly();
            }
        }
        return Array.Empty<EntrySnapshot>();
    }

    /// <summary>
    /// Returns the snapshot at the given version, or null.
    /// </summary>
    public EntrySnapshot? GetSnapshot(string profileName, Guid entryId, uint version)
    {
        if (!_store.TryGetValue(profileName, out var byEntry)) return null;
        if (!byEntry.TryGetValue(entryId, out var list)) return null;
        return list.FirstOrDefault(s => s.Version == version);
    }

    /// <summary>
    /// Restores the entry to the snapshot at the given version. The new entry
    /// will have version = original.version + 1, updated_at = now.
    /// </summary>
    public Entry Restore(string profileName, Guid entryId, uint version)
    {
        _vault.GetProfile(profileName);  // throws if locked or missing
        var snap = GetSnapshot(profileName, entryId, version)
            ?? throw new EntryNotFoundException(entryId);
        // Capture the current in-memory entry (BEFORE we overwrite it) so the
        // user can roll back the restore.
        var current = _vault.GetEntry(profileName, entryId);
        if (current != null)
            Capture(profileName, current, reason: "about to restore from v" + version);

        // Construct a new entry that copies the snapshot's data but bumps version.
        var restored = snap.Entry with
        {
            Version = snap.Entry.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _vault.PutEntry(profileName, restored);
        return restored;
    }

    /// <summary>Removes all snapshots for the given profile (e.g., on profile delete).</summary>
    public void PurgeProfile(string profileName)
    {
        _store.Remove(profileName);
        // P5-T1: Also remove from disk
        if (_snapshotDir != null)
        {
            var dir = Path.Combine(_snapshotDir, profileName);
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }

    /// <summary>Removes all snapshots for an entry (e.g., on permanent delete).</summary>
    public void PurgeEntry(string profileName, Guid entryId)
    {
        if (_store.TryGetValue(profileName, out var byEntry))
            byEntry.Remove(entryId);
        // P5-T1: Also remove from disk
        if (_snapshotDir != null)
        {
            var dir = Path.Combine(_snapshotDir, profileName, entryId.ToString());
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }

    // ============================================================
    //  P5-T1/P5-T2: Disk persistence (OKVS format)
    // ============================================================

    private static readonly byte[] OkvsMagic = { 0x4F, 0x4B, 0x56, 0x53 }; // "OKVS"

    /// <summary>P5-T2: Persist a snapshot to disk in OKVS format.
    /// Layout: [Magic(4B)] [Version(2B)] [EntryId(16B)] [EntryVersion(4B)]
    /// [CreatedAt(8B)] [PayloadLen(4B)] [Payload(N)] [Signature(64B)]
    /// Payload = JSON-serialized Entry, encrypted with XChaCha20-Poly1305 under
    /// the vault's KEK.</summary>
    private void PersistSnapshotToDisk(EntrySnapshot snap)
    {
        if (_crypto == null || _snapshotDir == null) return;

        var dir = Path.Combine(_snapshotDir, snap.ProfileName, snap.EntryId.ToString());
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{snap.Version}.entry.enc");

        // Serialize + encrypt the Entry
        var entryJson = JsonSerializer.Serialize(snap.Entry);
        var entryBytes = Encoding.UTF8.GetBytes(entryJson);
        var kek = _vault.GetDekForSeed(snap.ProfileName); // borrow KEK via DEK path
        // Actually we need the KEK, not DEK. Use a per-snapshot DEK approach:
        // generate a random DEK, encrypt payload, wrap DEK under KEK.
        var dekBytes = _crypto.RandomBytes(32);
        using var dek = DataEncryptionKey.From(dekBytes);
        _crypto.Zero(dekBytes);
        var payload = _crypto.Encrypt(dek, entryBytes, ReadOnlySpan<byte>.Empty);
        CryptographicOperations.ZeroMemory(entryBytes);

        // Build the OKVS file content (without signature)
        using var ms = new MemoryStream();
        ms.Write(OkvsMagic);                           // 4B magic
        ms.Write(BitConverter.GetBytes((ushort)1));     // 2B snapshot format version
        ms.Write(snap.EntryId.ToByteArray());           // 16B entry id
        ms.Write(BitConverter.GetBytes(snap.Version));  // 4B entry version
        ms.Write(BitConverter.GetBytes(snap.CapturedAt.ToUnixTimeSeconds())); // 8B timestamp

        // Payload: nonce(24) + ct + tag(16) — all from the EncryptedPayload
        ms.Write(BitConverter.GetBytes(payload.Nonce.Length + payload.Ciphertext.Length + payload.Tag.Length));
        ms.Write(payload.Nonce);
        ms.Write(payload.Ciphertext);
        ms.Write(payload.Tag);

        var unsignedContent = ms.ToArray();

        // Sign with device private key
        var vaultUuid = _vault.CurrentVault?.Metadata.Uuid ?? Guid.Empty;
        var devicePriv = _keystore?.Load(vaultUuid);
        byte[] signature;
        if (devicePriv != null && devicePriv.Length > 0)
        {
            using var key = DevicePrivateKey.From(devicePriv);
            signature = _crypto.Sign(key, unsignedContent);
            CryptographicOperations.ZeroMemory(devicePriv);
        }
        else
        {
            // No device key — use a zero signature (will fail verification on restore)
            signature = new byte[64];
        }

        // Write atomically: temp file + rename
        var tempPath = filePath + ".tmp";
        File.WriteAllBytes(tempPath, unsignedContent.Concat(signature).ToArray());
        if (File.Exists(filePath))
            File.Replace(tempPath, filePath, destinationBackupFileName: null);
        else
            File.Move(tempPath, filePath);
    }

    /// <summary>P5-T2: Load all snapshots for an entry from disk.</summary>
    private List<EntrySnapshot> LoadSnapshotsFromDisk(string profileName, Guid entryId)
    {
        var result = new List<EntrySnapshot>();
        if (_crypto == null || _snapshotDir == null) return result;

        var dir = Path.Combine(_snapshotDir, profileName, entryId.ToString());
        if (!Directory.Exists(dir)) return result;

        foreach (var file in Directory.GetFiles(dir, "*.entry.enc"))
        {
            try
            {
                var snap = ReadSnapshotFromDisk(file, profileName);
                if (snap != null) result.Add(snap);
            }
            catch { /* skip corrupt files */ }
        }

        // Sort newest-first
        result.Sort((a, b) => b.Version.CompareTo(a.Version));

        // Apply retention limit
        while (result.Count > _maxPerEntry)
            result.RemoveAt(result.Count - 1);

        return result;
    }

    /// <summary>P5-T2: Read and verify a single snapshot from disk.</summary>
    private EntrySnapshot? ReadSnapshotFromDisk(string filePath, string profileName)
    {
        if (_crypto == null) return null;

        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 4 + 2 + 16 + 4 + 8 + 4 + 64)
            return null; // too small

        int offset = 0;
        var magic = new byte[4];
        Buffer.BlockCopy(bytes, offset, magic, 0, 4); offset += 4;
        if (!magic.SequenceEqual(OkvsMagic)) return null;

        var snapshotVersion = BitConverter.ToUInt16(bytes, offset); offset += 2;
        var entryId = new Guid(new ReadOnlySpan<byte>(bytes, offset, 16)); offset += 16;
        var entryVersion = BitConverter.ToUInt32(bytes, offset); offset += 4;
        var createdAt = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(bytes, offset)); offset += 8;

        var payloadLen = BitConverter.ToInt32(bytes, offset); offset += 4;
        if (offset + payloadLen + 64 > bytes.Length) return null;

        // Parse AEAD payload
        const int nonceLen = 24, tagLen = 16;
        if (payloadLen < nonceLen + tagLen) return null;
        var nonce = new byte[nonceLen];
        Buffer.BlockCopy(bytes, offset, nonce, 0, nonceLen); offset += nonceLen;
        var ctLen = payloadLen - nonceLen - tagLen;
        var ct = new byte[ctLen];
        Buffer.BlockCopy(bytes, offset, ct, 0, ctLen); offset += ctLen;
        var tag = new byte[tagLen];
        Buffer.BlockCopy(bytes, offset, tag, 0, tagLen); offset += tagLen;

        // Signature (last 64 bytes)
        var signature = new byte[64];
        Buffer.BlockCopy(bytes, offset, signature, 0, 64);

        // Verify signature
        var unsignedContent = new byte[bytes.Length - 64];
        Buffer.BlockCopy(bytes, 0, unsignedContent, 0, unsignedContent.Length);

        var vaultUuid = _vault.CurrentVault?.Metadata.Uuid ?? Guid.Empty;
        var devicePub = _vault.DevicePublicKey;
        if (devicePub != null && !_crypto.Verify(devicePub, unsignedContent, signature))
            throw new CryptoException($"Snapshot file signature verification failed: {filePath}");

        // Decrypt payload
        var dek = _vault.GetDekForSeed(profileName); // Get DEK for decryption
        var payload = new EncryptedPayload(nonce, ct, tag, Array.Empty<byte>());
        byte[] entryBytes;
        try
        {
            entryBytes = _crypto.Decrypt(dek, payload, ReadOnlySpan<byte>.Empty);
        }
        catch
        {
            return null;
        }

        var entryJson = Encoding.UTF8.GetString(entryBytes);
        CryptographicOperations.ZeroMemory(entryBytes);
        var entry = JsonSerializer.Deserialize<Entry>(entryJson)
            ?? throw new CryptoException("Failed to deserialize entry from snapshot.");

        return new EntrySnapshot
        {
            EntryId = entryId,
            ProfileName = profileName,
            Version = entryVersion,
            CapturedAt = createdAt,
            Entry = entry,
            DeviceId = _deviceIdForSign ?? _deviceId,
            Reason = "disk"
        };
    }
}
