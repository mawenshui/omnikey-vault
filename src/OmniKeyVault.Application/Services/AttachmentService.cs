﻿using System.Security.Cryptography;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Encrypted blob storage for file attachments referenced by <c>file_ref</c>
/// fields. Per ROADMAP v0.3 S6-T4 / S6-T5 / MANUAL §4.2.1.
///
/// Each blob is stored on disk as a single file named
/// <c>&lt;sha256-hex-of-plaintext&gt;.bin</c>. The file body is a
/// double-wrapped envelope:
///
/// <code>
///   [wrapped_dek_nonce | wrapped_dek_ct | wrapped_dek_tag  // 24+N+16
///    payload_nonce    | payload_ct    | payload_tag]      // 24+M+16
/// </code>
///
/// The inner <em>payload</em> is the plaintext encrypted with a per-blob
/// <see cref="DataEncryptionKey"/>. The DEK itself is encrypted ("wrapped")
/// under the calling profile's <see cref="KeyEncryptionKey"/>, so the same
/// blob can be opened in any device that has access to the vault's master
/// password — the storage directory is effectively portable across devices
/// (MANUAL §4.2.1 portability goal).
/// </summary>
[OmniKeyVaultService]
public sealed class AttachmentService
{
    private readonly ICryptoProvider _crypto;
    private readonly string _storageDir;
    private readonly object _lock = new();

    // P4-T1: True LRU using LinkedList + Dictionary for O(1) access and
    // correct eviction order (least-recently-used is evicted, not FIFO).
    // The LinkedList maintains access order (front = LRU, back = MRU).
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, byte[] Bytes)> _lru = new(StringComparer.Ordinal);
    private const int LruCapacity = 16;

    public AttachmentService(ICryptoProvider crypto, string storageDirectory, Func<KeyEncryptionKey?>? kekProvider = null)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        if (string.IsNullOrEmpty(storageDirectory)) throw new ArgumentException("Storage directory required.", nameof(storageDirectory));
        StorageDirectory = storageDirectory;
        _storageDir = storageDirectory;
        _kekProvider = kekProvider;
        Directory.CreateDirectory(_storageDir);
    }

    private readonly Func<KeyEncryptionKey?>? _kekProvider;

    /// <summary>Absolute path of the directory where encrypted blobs are stored.</summary>
    public string StorageDirectory { get; }

    /// <summary>Save a blob. If <paramref name="kek"/> is null and a
    /// <c>kekProvider</c> was supplied at construction, the current KEK is
    /// fetched from the provider (e.g. <c>LockService.CurrentKek</c>). The
    /// blob id is the SHA-256 hex of the plaintext (stable across devices —
    /// same file → same id → automatic de-dup if the user uploads the same
    /// file twice). Returns the blob id to put in a <c>file_ref</c> field's
    /// <c>Value</c>.</summary>
    public string Save(string blobIdHint, byte[] plaintext, KeyEncryptionKey? kek = null,
        string? friendlyName = null, CancellationToken ct = default)
    {
        if (kek == null) kek = _kekProvider?.Invoke();
        if (kek == null) throw new VaultLockedException("Cannot save attachment: no KEK available (vault locked?).");
        return SaveInternal(plaintext, kek, friendlyName);
    }

    private string SaveInternal(byte[] plaintext, KeyEncryptionKey kek, string? friendlyName)
    {
        if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
        if (kek == null) throw new ArgumentNullException(nameof(kek));
        if (plaintext.Length == 0) throw new ValidationException("Cannot save empty attachment.");

        // 1) Hash the plaintext → stable blob id (also doubles as de-dup)
        var hash = SHA256.HashData(plaintext);
        var blobId = Convert.ToHexString(hash).ToLowerInvariant();
        var blobPath = Path.Combine(_storageDir, blobId + ".bin");

        // 2) Generate a per-blob DEK + nonce
        var dekBytes = _crypto.RandomBytes(32);
        using var dek = DataEncryptionKey.From(dekBytes);
        _crypto.Zero(dekBytes);

        // 3) Encrypt plaintext under the per-blob DEK
        var payload = _crypto.Encrypt(dek, plaintext, aad: ReadOnlySpan<byte>.Empty);

        // 4) Wrap (encrypt) the DEK under the profile's KEK. AEAD is
        // symmetric; re-wrap the KEK as a DEK for the type signature.
        EncryptedPayload wrappedDek;
        using (var kekAsDek = DataEncryptionKey.From(kek.ToArray()))
        {
            wrappedDek = _crypto.Encrypt(kekAsDek, dek.Span, aad: ReadOnlySpan<byte>.Empty);
        }

        // 5) P4-T7: Persist envelope atomically — write to a temp file, flush,
        //    then rename. A crash during write leaves no truncated .bin file.
        var tempPath = blobPath + ".tmp";
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            // wrapped_dek block
            fs.Write(wrappedDek.Nonce);
            fs.Write(wrappedDek.Ciphertext);
            fs.Write(wrappedDek.Tag);
            // payload block
            fs.Write(payload.Nonce);
            fs.Write(payload.Ciphertext);
            fs.Write(payload.Tag);
            fs.Flush(true);
        }
        // Atomic rename (File.Move with overwrite on .NET 8)
        if (File.Exists(blobPath))
            File.Replace(tempPath, blobPath, destinationBackupFileName: null);
        else
            File.Move(tempPath, blobPath);

        // 6) LRU cache a COPY of the plaintext (so PurgeCache cannot zero
        // the caller's array — the caller may keep their own reference for
        // re-use after Save returns).
        var cached = new byte[plaintext.Length];
        Buffer.BlockCopy(plaintext, 0, cached, 0, plaintext.Length);
        LruAddOrUpdate(blobId, cached);

        return blobId;
    }

    /// <summary>Read + decrypt a blob. If <paramref name="kek"/> is null and
    /// a <c>kekProvider</c> was supplied at construction, the current KEK is
    /// fetched from the provider. Returns null if the blob doesn't exist.</summary>
    public byte[]? Read(string blobId, KeyEncryptionKey? kek = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(blobId)) return null;
        if (kek == null) kek = _kekProvider?.Invoke();
        if (kek == null) throw new VaultLockedException("Cannot read attachment: no KEK available (vault locked?).");
        return ReadInternal(blobId, kek);
    }

    private byte[]? ReadInternal(string blobId, KeyEncryptionKey kek)
    {

        // 1) LRU hit — promote to most-recently-used (P4-T1)
        lock (_lock)
        {
            if (_lru.TryGetValue(blobId, out var entry))
            {
                // Promote to back of list (most recently used)
                _lruOrder.Remove(entry.Node);
                _lruOrder.AddLast(entry.Node);
                return entry.Bytes;
            }
        }

        var blobPath = Path.Combine(_storageDir, blobId + ".bin");
        if (!File.Exists(blobPath)) return null;

        // 2) Parse envelope (24 + 16 fixed for nonce / tag in XChaCha20-Poly1305)
        var bytes = File.ReadAllBytes(blobPath);
        if (bytes.Length < 24 + 24 + 32)
            throw new ValidationException("Attachment envelope too small — file may be corrupt.");

        // Layout: [wd_nonce(24) | wd_ct(N) | wd_tag(16) | p_nonce(24) | p_ct(M) | p_tag(16)]
        // We assume the per-blob DEK is exactly 32 bytes when wrapped, so wd_ct = 32.
        // The DEK is 32 bytes plaintext → 32 bytes ct + 16 bytes tag.
        const int nonceLen = 24, tagLen = 16, dekLen = 32;
        int wdCtLen = dekLen;  // ct length == plaintext length for stream cipher

        int offset = 0;
        var wdNonce = bytes.AsSpan(offset, nonceLen).ToArray(); offset += nonceLen;
        var wdCt = bytes.AsSpan(offset, wdCtLen).ToArray(); offset += wdCtLen;
        var wdTag = bytes.AsSpan(offset, tagLen).ToArray(); offset += tagLen;
        var pNonce = bytes.AsSpan(offset, nonceLen).ToArray(); offset += nonceLen;
        // remaining: pCt + pTag
        int pTotalLen = bytes.Length - offset;
        int pCtLen = pTotalLen - tagLen;
        if (pCtLen < 0) throw new ValidationException("Attachment envelope truncated.");
        var pCt = bytes.AsSpan(offset, pCtLen).ToArray(); offset += pCtLen;
        var pTag = bytes.AsSpan(offset, tagLen).ToArray();

        // 3) Unwrap DEK
        var wrappedDekPayload = new EncryptedPayload(wdNonce, wdCt, wdTag, Array.Empty<byte>());
        DataEncryptionKey dek;
        try
        {
            // ICryptoProvider.Decrypt takes a DataEncryptionKey, but AEAD is
            // symmetric — we re-wrap the KEK as a DEK-shaped key purely for
            // the type signature. The original KEK is not modified.
            using var kekAsDek = DataEncryptionKey.From(kek.ToArray());
            var dekBytes = _crypto.Decrypt(kekAsDek, wrappedDekPayload, aad: ReadOnlySpan<byte>.Empty);
            dek = DataEncryptionKey.From(dekBytes);
        }
        catch (Exception ex) when (ex is CryptoException)
        {
            throw new CryptoException("Failed to unwrap attachment DEK — wrong vault key?", ex);
        }

        // 4) Decrypt payload
        byte[] plaintext;
        try
        {
            var payload = new EncryptedPayload(pNonce, pCt, pTag, Array.Empty<byte>());
            plaintext = _crypto.Decrypt(dek, payload, aad: ReadOnlySpan<byte>.Empty);
        }
        catch (Exception ex) when (ex is CryptoException)
        {
            dek.Dispose();
            throw new CryptoException("Attachment payload AEAD failed — corrupted?", ex);
        }
        dek.Dispose();

        // 5) LRU — store a copy so PurgeCache doesn't zero the caller's
        // returned array (caller may still be using it).
        var lruCopy = new byte[plaintext.Length];
        Buffer.BlockCopy(plaintext, 0, lruCopy, 0, plaintext.Length);
        LruAddOrUpdate(blobId, lruCopy);

        return plaintext;
    }

    /// <summary>
    /// P4-T1: Add or update an entry in the true LRU cache. Evicts the
    /// least-recently-used entry (front of the LinkedList) when at capacity.
    /// </summary>
    private void LruAddOrUpdate(string blobId, byte[] bytes)
    {
        lock (_lock)
        {
            if (_lru.TryGetValue(blobId, out var existing))
            {
                // Update: zero old bytes, promote to MRU
                _crypto.Zero(existing.Bytes);
                _lruOrder.Remove(existing.Node);
                var node = _lruOrder.AddLast(blobId);
                _lru[blobId] = (node, bytes);
            }
            else
            {
                // New entry
                var node = _lruOrder.AddLast(blobId);
                _lru[blobId] = (node, bytes);

                // Evict LRU if over capacity
                if (_lru.Count > LruCapacity)
                {
                    var lruKey = _lruOrder.First!.Value;
                    var lruEntry = _lru[lruKey];
                    _crypto.Zero(lruEntry.Bytes);
                    _lruOrder.RemoveFirst();
                    _lru.Remove(lruKey);
                }
            }
        }
    }

    /// <summary>Returns the size (in bytes) of the encrypted on-disk file, or
    /// -1 if the blob is not in the storage directory.</summary>
    public long GetFileSize(string blobId)
    {
        if (string.IsNullOrEmpty(blobId)) return -1;
        var p = Path.Combine(_storageDir, blobId + ".bin");
        return File.Exists(p) ? new FileInfo(p).Length : -1;
    }

    /// <summary>Removes a blob from disk and the LRU. No-op if the blob
    /// doesn't exist (idempotent).</summary>
    public bool Delete(string blobId)
    {
        if (string.IsNullOrEmpty(blobId)) return false;
        var p = Path.Combine(_storageDir, blobId + ".bin");
        lock (_lock)
        {
            if (_lru.TryGetValue(blobId, out var entry))
            {
                _crypto.Zero(entry.Bytes);
                _lruOrder.Remove(entry.Node);
                _lru.Remove(blobId);
            }
        }
        if (!File.Exists(p)) return false;
        File.Delete(p);
        return true;
    }

    /// <summary>List all blob ids currently in the storage directory.</summary>
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_storageDir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_storageDir, "*.bin")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .ToList();
    }

    /// <summary>Zero all cached plaintexts and clear the LRU. Called by the
    /// GUI on lock to prevent secrets lingering in memory after the user
    /// steps away from the keyboard.</summary>
    public void PurgeCache()
    {
        lock (_lock)
        {
            foreach (var entry in _lru.Values)
                _crypto.Zero(entry.Bytes);
            _lru.Clear();
            _lruOrder.Clear();
        }
    }
}
