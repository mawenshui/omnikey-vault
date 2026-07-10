﻿﻿﻿﻿﻿using System.Security.Cryptography;
using System.Text;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Vault lifecycle service per ARCHITECTURE.md §4.2 / PRD §5.1.
/// Create / Unlock / Lock / Save / Load. Coordinates CryptoProvider, format,
/// and LockService.
///
/// Threading: NOT thread-safe for writes — caller must serialize. Single-process
/// model per ARCHITECTURE.md §5.1.
/// </summary>
[OmniKeyVaultService]
public sealed class VaultService : IDisposable
{
    private readonly ICryptoProvider _crypto;
    private readonly IVaultFormat _format;
    private readonly LockService _lock;
    private readonly ProfilePayloadCodec _codec;
    private readonly DeviceKeystore _keystore;
    private readonly string _deviceId;

    private Vault? _vault;
    private Dictionary<string, Profile> _profiles = new(StringComparer.Ordinal);
    private DeviceKeyPair? _deviceKeys;
    private Argon2Params _argon2Params = Argon2Params.Default;
    private byte[] _salt = Array.Empty<byte>();
    private byte[] _verifyTag = Array.Empty<byte>();
    private VectorClock _vectorClock = new();
    private string _vaultPath = string.Empty;
    private bool _disposed;

    public VaultService(ICryptoProvider crypto, IVaultFormat format, LockService lockService, ProfilePayloadCodec codec, string deviceId, DeviceKeystore? keystore = null)
    {
        _crypto = crypto;
        _format = format;
        _lock = lockService;
        _codec = codec;
        _keystore = keystore ?? new DeviceKeystore();
        _deviceId = deviceId;
    }

    public bool IsLoaded => _vault != null;
    public bool IsUnlocked => _lock.IsUnlocked;
    public string CurrentVaultPath => _vaultPath;
    public string DeviceId => _deviceId;
    public Argon2Params CurrentArgon2Params => _argon2Params;
    public VectorClock CurrentVectorClock => _vectorClock;
    public Vault? CurrentVault => _vault;
    public IReadOnlyDictionary<string, Profile> Profiles => _profiles;
    public DevicePublicKey? DevicePublicKey => _deviceKeys?.PublicKey;

    /// <summary>Creates a new Vault with the given master password. Outputs the vault UUID and recovery key.</summary>
    public async Task<CreateVaultResult> CreateAsync(string path, string name, byte[] masterPassword, Argon2Params? argon2Params = null, CancellationToken ct = default)
    {
        if (File.Exists(path))
            throw new ValidationException($"Vault file already exists: {path}");

        var args = argon2Params ?? Argon2Params.Default;
        if (args.Memory < 32 * 1024 * 1024)
            throw new ValidationException("Argon2id memory cost must be >= 32 MiB (INV-06 production requirement).");

        // 1. Salt + device keypair + vault UUID
        // Salt: 16 bytes for the KDF (libsodium constraint) + 16 bytes reserved (per OKV_FORMAT.md §4.1
        // salt slot is 32B; remaining 16B reserved for future PQ-upgrade pepper). Both halves are
        // random for v0.1 (the reserved half is unused but stored).
        var saltHalf = _crypto.RandomBytes((int)args.SaltLength);
        var saltReserved = _crypto.RandomBytes(16);
        _salt = new byte[32];
        Buffer.BlockCopy(saltHalf, 0, _salt, 0, 16);
        Buffer.BlockCopy(saltReserved, 0, _salt, 16, 16);
        CryptographicOperations.ZeroMemory(saltReserved);
        var deviceKeys = _crypto.GenerateDeviceKeyPair();
        var vaultUuid = _crypto.NewUuidV7();
        _argon2Params = args;
        _deviceKeys = deviceKeys;

        // Persist device private key to local keystore so subsequent CLI invocations can sign updates.
        _keystore.Save(vaultUuid, deviceKeys.PrivateKey.Span.ToArray());

        // 2. Derive MK + KEK (using the 16-byte KDF salt half).
        // NOTE: do not wrap these in `using` — ownership is transferred to LockService via
        // ActivateKeys. If we `using` them, the Dispose at end-of-method would zero the
        // material while LockService still holds a reference, leading to "DEK unwrap failed"
        // (zeroed KEK can no longer unwrap the on-disk DEK).
        var mk = _crypto.DeriveMasterKey(masterPassword, _salt.AsSpan(0, 16), args);
        var kek = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v1"), _salt.AsSpan(0, 16));
        var verifyTag = _crypto.ComputeVerifyTag(kek, Array.Empty<byte>());
        _verifyTag = verifyTag;

        // 3. Default prod profile
        var prodDekBytes = _crypto.RandomBytes(32);
        var prodDek = DataEncryptionKey.From(prodDekBytes);
        var wrappedDek = _crypto.WrapKey(kek, prodDek);
        var profileId = _crypto.NewUuidV7();
        var profileName = "prod";
        var now = DateTimeOffset.UtcNow;

        var emptyEntries = Array.Empty<Entry>();
        var emptyFolders = Array.Empty<Folder>();
        var emptyTags = Array.Empty<string>();
        var emptyTemplates = Array.Empty<Template>();
        var payloadBytes = _codec.Encode(emptyEntries, emptyFolders, emptyTags, emptyTemplates);
        var payload = _crypto.Encrypt(prodDek, payloadBytes, VaultCryptoHelpers.BuildProfileAad(vaultUuid, profileId));
        CryptographicOperations.ZeroMemory(payloadBytes);

        var prodProfile = new ProfileRecord
        {
            Id = profileId,
            Name = profileName,
            Color = ProfileColor.Green,
            Settings = ProfileSettings.DefaultProd(),
            WrappedDek = wrappedDek,
            PayloadNonce = payload.Nonce,
            PayloadTag = payload.Tag,
            EncryptedPayload = payload.Ciphertext
        };

        // 4. Recovery Key (32B CSPRNG, formatted as 8 groups of 8 chars base32 per SECURITY.md §9.2)
        var recoveryKeyBytes = _crypto.RandomBytes(32);
        var recoveryKey = FormatRecoveryKey(recoveryKeyBytes);

        // 5. Initial vector clock
        _vectorClock = new VectorClock().Increment(_deviceId);

        // 6. Persist
        _vaultPath = path;
        var record = new VaultRecord
        {
            AppBuildHash = _format.ComputeBuildHash(),
            VaultUuid = vaultUuid,
            Argon2Params = args,
            Salt = _salt,
            VerifyTag = verifyTag,
            DevicePublicKey = deviceKeys.PublicKey,
            Signature = new byte[64],  // filled in by _format.WriteAsync
            VectorClock = _vectorClock,
            Profiles = new List<ProfileRecord> { prodProfile }
        };
        await _format.WriteAsync(path, record, deviceKeys.PrivateKey, ct);

        // 7. Activate unlock state
        await _lock.ActivateKeysAsync(mk, kek, ct);
        _lock.CacheDek(profileName, prodDek);
        _lock.CacheDeviceKey(_deviceId, deviceKeys.PrivateKey);

        // 8. Build in-memory Vault
        _vault = new Vault
        {
            Metadata = new VaultMetadata
            {
                Uuid = vaultUuid,
                Name = name,
                CreatedAt = now,
                SchemaVersion = 1
            },
            VectorClock = _vectorClock,
            Profiles = new Dictionary<string, Profile>(StringComparer.Ordinal)
            {
                [profileName] = new Profile
                {
                    Id = profileId,
                    Name = profileName,
                    Color = ProfileColor.Green,
                    Settings = ProfileSettings.DefaultProd(),
                    Entries = Array.Empty<Entry>(),
                    Folders = Array.Empty<Folder>(),
                    Templates = Array.Empty<Template>()
                }
            }
        };
        _profiles = new Dictionary<string, Profile>(_vault.Profiles, StringComparer.Ordinal);

        CryptographicOperations.ZeroMemory(recoveryKeyBytes);
        return new CreateVaultResult(vaultUuid, recoveryKey, new[] { profileName });
    }

    /// <summary>Unlocks an existing Vault with the given master password.</summary>
    public async Task<UnlockResult> UnlockAsync(string path, byte[] masterPassword, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new ValidationException($"Vault file not found: {path}");

        var record = await _format.ReadAsync(path, ct);

        // Verify Ed25519 signature first (SECURITY.md §13.2 / threat T8).
        var signedRegion = record.SignedRegion ?? throw new CryptoException("Vault file is missing signed-region metadata (corrupt or unsupported version).");
        if (!_crypto.Verify(record.DevicePublicKey, signedRegion, record.Signature))
            throw new CryptoException("Vault signature verification failed — file may have been tampered with.");

        // The salt slot is 32B; only the first 16B are the actual KDF salt (per v0.1 deviation note).
        var kdfSalt = record.Salt.AsSpan(0, 16);

        // Derive MK + KEK from the supplied password.
        // NOTE: do not wrap in `using` — ownership transfers to LockService via ActivateKeys.
        var mk = _crypto.DeriveMasterKey(masterPassword, kdfSalt, record.Argon2Params);
        var kek = _crypto.DeriveKek(mk, Encoding.UTF8.GetBytes("okv-kek-v1"), kdfSalt);

        // Verify the master password via the stored verify tag (constant-time per INV-07)
        var computed = _crypto.ComputeVerifyTag(kek, Array.Empty<byte>());
        if (!_crypto.FixedTimeEquals(computed, record.VerifyTag))
            throw new CryptoException("Master password is incorrect.");

        // Activate unlock state FIRST so subsequent CacheDek calls don't trip EnsureUnlocked.
        await _lock.ActivateKeysAsync(mk, kek, ct);

        // Decrypt each profile
        _profiles = new Dictionary<string, Profile>(StringComparer.Ordinal);
        foreach (var pr in record.Profiles)
        {
            using var dek = _crypto.UnwrapKey(kek, pr.WrappedDek);
            var payload = new EncryptedPayload(pr.PayloadNonce, pr.EncryptedPayload, pr.PayloadTag, VaultCryptoHelpers.BuildProfileAad(record.VaultUuid, pr.Id));
            var bodyBytes = _crypto.Decrypt(dek, in payload, VaultCryptoHelpers.BuildProfileAad(record.VaultUuid, pr.Id));
            var (entries, folders, tags, templates) = _codec.Decode(bodyBytes);
            CryptographicOperations.ZeroMemory(bodyBytes);

            _profiles[pr.Name] = new Profile
            {
                Id = pr.Id,
                Name = pr.Name,
                Color = pr.Color,
                Settings = pr.Settings,
                Entries = entries,
                Folders = folders,
                Templates = templates
            };
            // Cache the DEK so subsequent EntryService writes can re-encrypt.
            // Copy to a fresh DataEncryptionKey because `dek` is disposed at end of iteration.
            _lock.CacheDek(pr.Name, DataEncryptionKey.From(dek.Span.ToArray()));
        }

        _argon2Params = record.Argon2Params;
        _salt = record.Salt;
        _verifyTag = record.VerifyTag;
        _vectorClock = record.VectorClock;
        _vaultPath = path;

        // Load the device private key from the local keystore (created by CreateAsync in this
        // device). If absent (e.g., the vault was first created on another device, or the
        // keystore was deleted), we fall back to a keyless state — read-only operations work
        // but writes are rejected with "Device private key not available." until the user
        // imports/regenerates the key. For v0.1 MVP we do NOT auto-regenerate (that would
        // change the public key in the .okv header, breaking sync).
        var devicePrivBytes = _keystore.Load(record.VaultUuid);
        var devicePriv = devicePrivBytes != null ? DevicePrivateKey.From(devicePrivBytes) : null;
        if (devicePriv != null)
            _lock.CacheDeviceKey(_deviceId, devicePriv);
        _deviceKeys = new DeviceKeyPair(record.DevicePublicKey, devicePriv ?? DevicePrivateKey.From(Array.Empty<byte>()));
        // Note: when devicePriv is empty, SaveAsync will throw "Device private key not available"
        // (handled at SaveAsync). Read-only commands (list, get) work fine.

        _vault = new Vault
        {
            Metadata = new VaultMetadata
            {
                Uuid = record.VaultUuid,
                Name = record.VaultUuid.ToString(),  // v0.1 MVP: name is the UUID; user-facing name comes later
                CreatedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1
            },
            VectorClock = _vectorClock,
            Profiles = new Dictionary<string, Profile>(_profiles, StringComparer.Ordinal)
        };

        return new UnlockResult(record.VaultUuid, _profiles.Keys.OrderBy(k => k).ToList(), record.VectorClock);
    }

    /// <summary>Locks the Vault: zeroes all in-memory keys AND sensitive
    /// field values (INV-03: Lock zeroes all sensitive data).
    /// After Lock returns, no sensitive byte[] remains in heap memory
    /// reachable through VaultService fields.</summary>
    public void Lock()
    {
        // INV-03: Zero all sensitive field values before dropping references.
        // Field.Value is byte[] (P6-T1), so we can zero it in-place.
        foreach (var profile in _profiles.Values)
        {
            foreach (var entry in profile.Entries)
            {
                foreach (var field in entry.Fields)
                {
                    if (field.Sensitive && field.Value != null && field.Value.Length > 0)
                    {
                        CryptographicOperations.ZeroMemory(field.Value);
                    }
                }
            }
        }

        _lock.Lock();
        _vault = null;
        _profiles = new Dictionary<string, Profile>(StringComparer.Ordinal);
        _deviceKeys = null;
    }

    /// <summary>v0.2 (S7-T3): change the master password. Re-derives MK + KEK
    /// from the new password, re-encrypts every profile's DEK with the new
    /// KEK, and atomically rewrites the vault file. The vault must be unlocked.
    /// The salt + Argon2Params are preserved (changing them would require
    /// re-encrypting every profile payload, deferred to v0.3).</summary>
    public async Task ChangePasswordAsync(byte[] oldPassword, byte[] newPassword, CancellationToken ct = default)
    {
        if (oldPassword == null) throw new ArgumentNullException(nameof(oldPassword));
        if (newPassword == null) throw new ArgumentNullException(nameof(newPassword));
        if (!_lock.IsUnlocked) throw new InvalidOperationException("Vault is locked.");
        if (_deviceKeys == null || _deviceKeys.PrivateKey.Span.Length == 0)
            throw new InvalidOperationException("Device private key not available.");
        if (_vaultPath == null) throw new InvalidOperationException("No vault loaded.");

        // 1. Verify old password against the stored verify tag.
        var oldMk = _crypto.DeriveMasterKey(oldPassword, _salt.AsSpan(0, 16), _argon2Params);
        var oldKek = _crypto.DeriveKek(oldMk, Encoding.UTF8.GetBytes("okv-kek-v1"), _salt.AsSpan(0, 16));
        var computed = _crypto.ComputeVerifyTag(oldKek, Array.Empty<byte>());
        if (!_crypto.FixedTimeEquals(computed, _verifyTag))
        {
            oldMk.Dispose();
            oldKek.Dispose();
            throw new CryptoException("Old master password is incorrect.");
        }
        // We do NOT need the old MK or old KEK once verified; the in-memory
        // _lock still has the originals so we can decrypt the current DEK
        // cache and re-wrap it.
        oldMk.Dispose();
        oldKek.Dispose();

        // 2. Derive new MK + KEK.
        // P4-T2: Do NOT use `using` — ownership transfers to LockService via
        // ActivateKeysAsync on the success path. On the exception path, the
        // catch block disposes them to prevent key material leakage.
        var newMk = _crypto.DeriveMasterKey(newPassword, _salt.AsSpan(0, 16), _argon2Params);
        var newKek = _crypto.DeriveKek(newMk, Encoding.UTF8.GetBytes("okv-kek-v1"), _salt.AsSpan(0, 16));
        try
        {
        var newVerifyTag = _crypto.ComputeVerifyTag(newKek, Array.Empty<byte>());

        // 3. Build the updated record: re-wrap every profile's DEK with the new KEK.
        //    The DEK is read from the lock service (still keyed on the OLD KEK).
        //    We re-wrap with the NEW KEK. The profile payload encryption is unchanged
        //    (still uses the same DEK), so we don't need to re-encrypt any payloads.
        var newProfileRecords = new List<ProfileRecord>(_vault!.Profiles.Count);
        // Save the DEKs so we can re-cache them after ActivateKeys drops the cache.
        var deksToRecache = new Dictionary<string, DataEncryptionKey>(StringComparer.Ordinal);
        foreach (var (name, profile) in _vault.Profiles)
        {
            if (!_lock.TryGetDek(name, out var dek) || dek == null)
                throw new InvalidOperationException($"DEK for profile '{name}' is not in cache.");
            // Copy the DEK (TryGetDek returns the same instance; we'll cache our own)
            var dekCopy = DataEncryptionKey.From(dek.Span.ToArray());
            deksToRecache[name] = dekCopy;
            var newWrappedDek = _crypto.WrapKey(newKek, dekCopy);
            newProfileRecords.Add(new ProfileRecord
            {
                Id = profile.Id,
                Name = profile.Name,
                Color = profile.Color,
                Settings = profile.Settings,
                WrappedDek = newWrappedDek,
                PayloadNonce = Array.Empty<byte>(),  // placeholder; replaced below
                PayloadTag = Array.Empty<byte>(),
                EncryptedPayload = Array.Empty<byte>(),
            });
        }

        // 4. Re-encrypt each profile's payload with the same DEK (no
        //    payload change) so the on-disk format is internally consistent.
        for (int i = 0; i < newProfileRecords.Count; i++)
        {
            var name = newProfileRecords[i].Name;
            var profile = _vault.Profiles[name];
            var dek = deksToRecache[name];
            var tagsList = VaultCryptoHelpers.CollectTags(profile.Entries);
            var payloadBytes = _codec.Encode(
                profile.Entries, profile.Folders,
                tagsList,
                profile.Templates);
            var encrypted = _crypto.Encrypt(dek, payloadBytes, VaultCryptoHelpers.BuildProfileAad(_vault.Metadata.Uuid, profile.Id));
            CryptographicOperations.ZeroMemory(payloadBytes);
            newProfileRecords[i] = newProfileRecords[i] with
            {
                PayloadNonce = encrypted.Nonce,
                PayloadTag = encrypted.Tag,
                EncryptedPayload = encrypted.Ciphertext,
            };
        }

        // 5. Build the updated VaultRecord.
        var updatedRecord = new VaultRecord
        {
            AppBuildHash = _format.ComputeBuildHash(),
            VaultUuid = _vault.Metadata.Uuid,
            Argon2Params = _argon2Params,
            Salt = _salt,
            VerifyTag = newVerifyTag,
            DevicePublicKey = _deviceKeys.PublicKey,
            Signature = new byte[64],  // filled by _format.WriteAsync
            VectorClock = _vectorClock.Increment(_deviceId),
            Profiles = newProfileRecords,
        };

        // 6. Persist atomically (vault will be locked for a brief moment).
        await _format.WriteAsync(_vaultPath, updatedRecord, _deviceKeys.PrivateKey, ct);

        // 7. Activate the new lock state, then re-cache DEKs.
        //    Ownership of newMk/newKek transfers to LockService — do NOT dispose.
        await _lock.ActivateKeysAsync(newMk, newKek, ct);
        foreach (var (name, dek) in deksToRecache)
            _lock.CacheDek(name, dek);
        _verifyTag = newVerifyTag;
        }
        catch
        {
            // Exception path: dispose new keys to prevent leakage.
            newMk.Dispose();
            newKek.Dispose();
            throw;
        }

        // 8. Rebuild the in-memory Vault model.
        var newProfiles = new Dictionary<string, Profile>(StringComparer.Ordinal);
        foreach (var (name, profile) in _vault.Profiles)
        {
            newProfiles[name] = profile;  // entries are unchanged
        }
        _vault = new Vault
        {
            Metadata = _vault.Metadata,
            VectorClock = _vectorClock,
            Profiles = newProfiles,
        };
        _profiles = newProfiles;
    }

    /// <summary>Saves the current in-memory state to disk (atomic).</summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        _lock.EnsureUnlocked();
        if (_vault == null) throw new ValidationException("No vault loaded.");

        // NOTE: _lock.CurrentKek is owned by LockService; do NOT dispose it.
        // We use it via a local reference; LockService handles disposal.
        var kek = _lock.CurrentKek ?? throw new VaultLockedException("KEK not available.");
        var profileRecords = new List<ProfileRecord>();
        foreach (var (name, p) in _profiles)
        {
            var dek = _lock.GetDek(name);
            var tags = VaultCryptoHelpers.CollectTags(p.Entries);
            var payloadBytes = _codec.Encode(p.Entries, p.Folders, tags, p.Templates);
            var aad = VaultCryptoHelpers.BuildProfileAad(_vault.Metadata.Uuid, p.Id);
            var payload = _crypto.Encrypt(dek, payloadBytes, aad);
            CryptographicOperations.ZeroMemory(payloadBytes);

            var wrappedDek = _crypto.WrapKey(kek, dek);
            profileRecords.Add(new ProfileRecord
            {
                Id = p.Id,
                Name = p.Name,
                Color = p.Color,
                Settings = p.Settings,
                WrappedDek = wrappedDek,
                PayloadNonce = payload.Nonce,
                PayloadTag = payload.Tag,
                EncryptedPayload = payload.Ciphertext
            });
        }

        _vectorClock = _vectorClock.Increment(_deviceId);

        var record = new VaultRecord
        {
            AppBuildHash = _format.ComputeBuildHash(),
            VaultUuid = _vault.Metadata.Uuid,
            Argon2Params = _argon2Params,
            Salt = _salt,
            VerifyTag = _verifyTag,
            DevicePublicKey = _deviceKeys?.PublicKey ?? throw new InvalidOperationException("Device keys not initialized."),
            Signature = new byte[64],
            VectorClock = _vectorClock,
            Profiles = profileRecords
        };

        var devicePriv = _lock.GetDeviceKey(_deviceId) ?? throw new VaultLockedException("Device private key not available.");
        await _format.WriteAsync(_vaultPath, record, devicePriv, ct);
    }

    /// <summary>Updates an entry in the named profile and re-encrypts its section.</summary>
    public void PutEntry(string profileName, Entry entry)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var profile))
            throw new ProfileNotFoundException(profileName);

        var existing = profile.Entries.FirstOrDefault(e => e.Id == entry.Id);
        var newEntries = existing == null
            ? profile.Entries.Append(entry).ToList()
            : profile.Entries.Select(e => e.Id == entry.Id ? entry : e).ToList();

        _profiles[profileName] = new Profile
        {
            Id = profile.Id,
            Name = profile.Name,
            Color = profile.Color,
            Settings = profile.Settings,
            Entries = newEntries,
            Folders = profile.Folders,
            Templates = profile.Templates
        };
    }

    /// <summary>Removes the entry with the given id from the named profile.</summary>
    public void DeleteEntry(string profileName, Guid entryId)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var profile))
            throw new ProfileNotFoundException(profileName);
        var newEntries = profile.Entries.Where(e => e.Id != entryId).ToList();
        _profiles[profileName] = new Profile
        {
            Id = profile.Id,
            Name = profile.Name,
            Color = profile.Color,
            Settings = profile.Settings,
            Entries = newEntries,
            Folders = profile.Folders,
            Templates = profile.Templates
        };
    }

    /// <summary>Returns the entry with the given id, or null.</summary>
    public Entry? GetEntry(string profileName, Guid entryId)
    {
        _lock.EnsureUnlocked();
        return _profiles.TryGetValue(profileName, out var p) ? p.FindEntry(entryId) : null;
    }

    /// <summary>Returns all entries in the named profile.</summary>
    public IReadOnlyList<Entry> ListEntries(string profileName)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var p))
            throw new ProfileNotFoundException(profileName);
        return p.Entries;
    }

    public Profile GetProfile(string profileName)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var p))
            throw new ProfileNotFoundException(profileName);
        return p;
    }

    /// <summary>
    /// Internal helper used by SeedExporter to grab the current DEK for a profile
    /// without exposing the LockService to the application layer.
    /// </summary>
    internal DataEncryptionKey GetDekForSeed(string profileName)
    {
        _lock.EnsureUnlocked();
        return _lock.GetDek(profileName);
    }

    // ---- Profile management (PRD §5.1, v0.2) ----

    /// <summary>
    /// Returns the names of all profiles in the vault, sorted alphabetically.
    /// </summary>
    public IReadOnlyList<string> ListProfileNames()
    {
        _lock.EnsureUnlocked();
        return _profiles.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Returns the names of profiles that participate in sync (per ProfileSettings).
    /// </summary>
    public IReadOnlyList<string> ListSyncableProfileNames()
    {
        _lock.EnsureUnlocked();
        return _profiles.Values
            .Where(p => p.Settings.ParticipateInSync)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Creates a new profile with a fresh random DEK, wrapped with KEK. Caller must <see cref="SaveAsync"/> afterwards.
    /// </summary>
    public Profile CreateProfile(string name, ProfileColor color, ProfileSettings? settings = null)
    {
        _lock.EnsureUnlocked();
        if (string.IsNullOrEmpty(name)) throw new ValidationException("Profile name is required.");
        if (_profiles.ContainsKey(name))
            throw new NameConflictException($"Profile '{name}' already exists.");

        // Generate a fresh DEK for this profile, wrap with current KEK, cache.
        var kek = _lock.CurrentKek ?? throw new VaultLockedException("KEK not available.");
        var dekBytes = _crypto.RandomBytes(32);
        var dek = DataEncryptionKey.From(dekBytes);
        var wrappedDek = _crypto.WrapKey(kek, dek);
        _lock.CacheDek(name, dek);

        var profileId = _crypto.NewUuidV7();
        var profileSettings = settings ?? DefaultSettingsForName(name);
        var profile = new Profile
        {
            Id = profileId,
            Name = name,
            Color = color,
            Settings = profileSettings,
            Entries = Array.Empty<Entry>(),
            Folders = Array.Empty<Folder>(),
            Templates = Array.Empty<Template>()
        };
        _profiles[name] = profile;
        CryptographicOperations.ZeroMemory(dekBytes);
        return profile;
    }

    /// <summary>
    /// Deletes a profile and disposes its DEK. The last profile cannot be deleted
    /// (a vault must always have at least one profile).
    /// </summary>
    public void DeleteProfile(string name)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.ContainsKey(name))
            throw new ProfileNotFoundException(name);
        if (_profiles.Count <= 1)
            throw new ValidationException("Cannot delete the last profile in a vault.");

        _profiles.Remove(name);
        // Dispose the DEK so the key material is zeroed (INV-03).
        var dek = _lock.GetDek(name);
        _lock.RemoveDek(name);
        dek.Dispose();
    }

    /// <summary>
    /// Updates the settings (sync participation, auto-lock, idle minutes) for an existing profile.
    /// </summary>
    public void UpdateProfileSettings(string name, ProfileSettings settings)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(name, out var existing))
            throw new ProfileNotFoundException(name);
        _profiles[name] = new Profile
        {
            Id = existing.Id,
            Name = existing.Name,
            Color = existing.Color,
            Settings = settings,
            Entries = existing.Entries,
            Folders = existing.Folders,
            Templates = existing.Templates
        };
    }

    // ---- Folder management (MANUAL §10.2 / OKV_FORMAT §3.6, v0.2) ----

    /// <summary>Returns all folders in the named profile, ordered by name.</summary>
    public IReadOnlyList<Folder> ListFolders(string profileName)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var p))
            return Array.Empty<Folder>();
        return p.Folders.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Creates a new folder. Names are unique per (profile, parent).</summary>
    public Folder CreateFolder(string profileName, string folderName, Guid? parentId = null)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var p))
            throw new ProfileNotFoundException(profileName);
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ValidationException("Folder name is required.");
        if (p.Folders.Any(f => string.Equals(f.Name, folderName, StringComparison.Ordinal) && f.ParentId == parentId))
            throw new NameConflictException($"Folder '{folderName}' already exists in this scope.");

        var folder = new Folder { Id = Guid.NewGuid(), Name = folderName.Trim(), ParentId = parentId };
        _profiles[profileName] = new Profile
        {
            Id = p.Id,
            Name = p.Name,
            Color = p.Color,
            Settings = p.Settings,
            Entries = p.Entries,
            Folders = p.Folders.Append(folder).ToList(),
            Templates = p.Templates
        };
        return folder;
    }

    /// <summary>Renames a folder.</summary>
    public void RenameFolder(string profileName, Guid folderId, string newName)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var p))
            throw new ProfileNotFoundException(profileName);
        if (string.IsNullOrWhiteSpace(newName))
            throw new ValidationException("Folder name is required.");
        var existing = p.Folders.FirstOrDefault(f => f.Id == folderId)
            ?? throw new ValidationException($"Folder {folderId} not found.");
        if (p.Folders.Any(f => f.Id != folderId
            && string.Equals(f.Name, newName, StringComparison.Ordinal)
            && f.ParentId == existing.ParentId))
            throw new NameConflictException($"Folder '{newName}' already exists in this scope.");

        var renamed = existing with { Name = newName.Trim() };
        _profiles[profileName] = new Profile
        {
            Id = p.Id,
            Name = p.Name,
            Color = p.Color,
            Settings = p.Settings,
            Entries = p.Entries,
            Folders = p.Folders.Select(f => f.Id == folderId ? renamed : f).ToList(),
            Templates = p.Templates
        };
    }

    /// <summary>Deletes a folder. Entries in the folder are moved to the parent
    /// (or root when the folder has no parent). Sub-folders are deleted recursively.</summary>
    public void DeleteFolder(string profileName, Guid folderId)
    {
        _lock.EnsureUnlocked();
        if (!_profiles.TryGetValue(profileName, out var p))
            throw new ProfileNotFoundException(profileName);
        var target = p.Folders.FirstOrDefault(f => f.Id == folderId)
            ?? throw new ValidationException($"Folder {folderId} not found.");

        // Collect the closure of folder ids being deleted.
        var toDelete = new HashSet<Guid> { folderId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var f in p.Folders)
            {
                if (f.ParentId.HasValue && toDelete.Contains(f.ParentId.Value) && toDelete.Add(f.Id))
                    changed = true;
            }
        }

        // Move entries in deleted folders up to the target's parent.
        var newEntries = p.Entries
            .Select(e => (e.Folder.HasValue && toDelete.Contains(e.Folder.Value))
                ? e with { Folder = target.ParentId, Version = e.Version + 1, UpdatedAt = DateTimeOffset.UtcNow }
                : e)
            .ToList();
        var newFolders = p.Folders.Where(f => !toDelete.Contains(f.Id)).ToList();
        _profiles[profileName] = new Profile
        {
            Id = p.Id,
            Name = p.Name,
            Color = p.Color,
            Settings = p.Settings,
            Entries = newEntries,
            Folders = newFolders,
            Templates = p.Templates
        };
    }

    /// <summary>
    /// Returns the entry count of each profile (for CLI `profile list`).
    /// </summary>
    public IReadOnlyDictionary<string, int> GetEntryCounts()
    {
        _lock.EnsureUnlocked();
        return _profiles.ToDictionary(p => p.Key, p => p.Value.Entries.Count, StringComparer.Ordinal);
    }

    private static ProfileSettings DefaultSettingsForName(string name) => name switch
    {
        "prod" => ProfileSettings.DefaultProd(),
        "dev" or "test" => ProfileSettings.DefaultDev(),
        _ => new ProfileSettings
        {
            ParticipateInSync = true,
            AutoLockOnSwitch = false,
            IdleLockMinutes = 15
        }
    };


    private static string FormatRecoveryKey(byte[] bytes)
    {
        // Phase 12: 32 bytes → base32 (RFC 4648) → 52 chars (no padding),
        // grouped as 13 groups of 4 chars separated by dashes.
        // Format: XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
        var base32 = Base32Encode(bytes);
        var groups = new List<string>();
        for (int i = 0; i < base32.Length; i += 4)
            groups.Add(base32.Substring(i, Math.Min(4, base32.Length - i)));
        return string.Join("-", groups);
    }

    /// <summary>Encodes raw bytes to a base32 string (RFC 4648, no padding).</summary>
    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder();
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                sb.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
            sb.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _deviceKeys?.PrivateKey.Dispose();
        _disposed = true;
    }
}

public sealed record CreateVaultResult(Guid VaultUuid, string RecoveryKey, IReadOnlyList<string> Profiles);

public sealed record UnlockResult(Guid VaultUuid, IReadOnlyList<string> Profiles, VectorClock VectorClock);
