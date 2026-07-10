﻿using System.Collections.Concurrent;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Manages the unlock window per ARCHITECTURE.md §4.2 / SECURITY.md §4.4.
/// Holds MK / KEK / per-profile DEKs in memory while the Vault is unlocked.
/// Locking zeroes all key material (INV-04: locked service calls throw).
/// </summary>
[OmniKeyVaultService]
public sealed class LockService : IDisposable
{
    private readonly ICryptoProvider _crypto;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MasterKey? _mk;
    private KeyEncryptionKey? _kek;
    private readonly ConcurrentDictionary<string, DataEncryptionKey> _deks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DevicePrivateKey> _deviceKeys = new(StringComparer.Ordinal);
    private bool _disposed;

    public LockService(ICryptoProvider crypto)
    {
        _crypto = crypto;
    }

    public bool IsUnlocked => _mk != null && _kek != null;

    public MasterKey? CurrentMasterKey => _mk;
    public KeyEncryptionKey? CurrentKek => _kek;

    /// <summary>Throws VaultLockedException if not unlocked. Called by all Service write methods.</summary>
    public void EnsureUnlocked()
    {
        if (!IsUnlocked)
            throw new VaultLockedException("Vault is locked. Run `okv vault unlock` first.");
    }

    /// <summary>Activates a freshly derived MK + KEK (from create/unlock).
    /// P4-T4: async to avoid ThreadPool starvation from _gate.Wait().</summary>
    public async Task ActivateKeysAsync(MasterKey mk, KeyEncryptionKey kek, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Drop any prior keys first.
            LockInternal();
            _mk = mk;
            _kek = kek;
        }
        finally { _gate.Release(); }
    }

    public void CacheDek(string profileName, DataEncryptionKey dek)
    {
        EnsureUnlocked();
        _deks[profileName] = dek;
    }

    public DataEncryptionKey GetDek(string profileName)
    {
        EnsureUnlocked();
        if (!_deks.TryGetValue(profileName, out var dek))
            throw new VaultLockedException($"DEK for profile '{profileName}' not cached. The profile may need to be unwrapped.");
        return dek;
    }

    public bool TryGetDek(string profileName, out DataEncryptionKey? dek)
    {
        if (!IsUnlocked) { dek = null; return false; }
        return _deks.TryGetValue(profileName, out dek);
    }

    /// <summary>Removes the DEK for the given profile. The caller is responsible for disposing the returned DEK.</summary>
    public DataEncryptionKey? RemoveDek(string profileName)
    {
        EnsureUnlocked();
        if (_deks.TryRemove(profileName, out var dek)) return dek;
        return null;
    }

    public void CacheDeviceKey(string deviceId, DevicePrivateKey key)
    {
        EnsureUnlocked();
        _deviceKeys[deviceId] = key;
    }

    public DevicePrivateKey? GetDeviceKey(string deviceId)
    {
        return _deviceKeys.TryGetValue(deviceId, out var k) ? k : null;
    }

    /// <summary>Locks the Vault: zeroes all keys (INV-04 / SECURITY.md §6.3).
    /// P4-T4: async to avoid ThreadPool starvation from _gate.Wait().</summary>
    public async Task LockAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { LockInternal(); }
        finally { _gate.Release(); }
    }

    /// <summary>Sync wrapper for backward-compatible callers (GUI event handlers,
    /// tests). The operation is CPU-only (dispose + clear) and the semaphore is
    /// almost never contended, so GetAwaiter().GetResult() completes
    /// synchronously without blocking a ThreadPool thread.</summary>
    public void Lock() => LockAsync().GetAwaiter().GetResult();

    private void LockInternal()
    {
        foreach (var d in _deks.Values) d.Dispose();
        foreach (var k in _deviceKeys.Values) k.Dispose();
        _deks.Clear();
        _deviceKeys.Clear();
        _mk?.Dispose();
        _kek?.Dispose();
        _mk = null;
        _kek = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        LockInternal();
        _gate.Dispose();
        _disposed = true;
    }
}
