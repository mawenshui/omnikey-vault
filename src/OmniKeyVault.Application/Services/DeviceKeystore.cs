using System.Text.Json;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v0.1 MVP device keystore. Stores the device's Ed25519 private key in a local
/// file keyed by the vault UUID. The private key is stored in plaintext for v0.1;
/// a future version will wrap it with the KEK derived from the master password.
///
/// File location: %APPDATA%\OmniKeyVault\device-keys\&lt;vault-uuid&gt;.key
/// File format: { "vault_uuid": "...", "private_key": "&lt;base64&gt;", "created_at": "..." }
///
/// v0.1 MVP deviation from SECURITY.md §4.2: device key stored in plaintext.
/// Wrapping with KEK requires the master password on first install; for v0.1 we
/// accept the trade-off (single-machine MVP, device key is bound to the device's
/// filesystem, not transmitted). See test report §A.2.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class DeviceKeystore
{
    private readonly string _keystoreDir;

    public DeviceKeystore()
    {
        _keystoreDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", "device-keys");
        Directory.CreateDirectory(_keystoreDir);
    }

    public string KeyFilePath(Guid vaultUuid) => Path.Combine(_keystoreDir, $"{vaultUuid}.key");

    public bool Exists(Guid vaultUuid) => File.Exists(KeyFilePath(vaultUuid));

    public void Save(Guid vaultUuid, byte[] privateKey)
    {
        var record = new KeystoreRecord
        {
            VaultUuid = vaultUuid,
            PrivateKeyBase64 = Convert.ToBase64String(privateKey),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(record, JsonOpts);
        File.WriteAllText(KeyFilePath(vaultUuid), json);
    }

    public byte[]? Load(Guid vaultUuid)
    {
        if (!Exists(vaultUuid)) return null;
        var json = File.ReadAllText(KeyFilePath(vaultUuid));
        var record = JsonSerializer.Deserialize<KeystoreRecord>(json, JsonOpts);
        if (record == null) return null;
        return Convert.FromBase64String(record.PrivateKeyBase64);
    }

    public void Delete(Guid vaultUuid)
    {
        var path = KeyFilePath(vaultUuid);
        if (File.Exists(path)) File.Delete(path);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private sealed class KeystoreRecord
    {
        public Guid VaultUuid { get; set; }
        public string PrivateKeyBase64 { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
