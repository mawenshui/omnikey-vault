using OmniKeyVault.Domain;

namespace OmniKeyVault.Contracts;

/// <summary>
/// Persisted representation of a Vault. The header fields are public
/// because they live in the fixed .okv binary header per OKV_FORMAT.md §4.1.
/// Profile payload bytes are AEAD-encrypted and parsed separately.
/// </summary>
public sealed record VaultRecord
{
    /// <summary>Header version: 1 = single Argon2id KDF (v1.0-v1.5), 2 = double Argon2id key stretching (v1.6+).</summary>
    public ushort HeaderVersion { get; init; } = 2;
    public required byte[] AppBuildHash { get; init; }      // 8 bytes
    public required Guid VaultUuid { get; init; }
    public required Argon2Params Argon2Params { get; init; }
    public required byte[] Salt { get; init; }              // 32 bytes (first 16 used by KDF; rest reserved per v0.1 deviation)
    public required byte[] VerifyTag { get; init; }         // 32 bytes
    public required DevicePublicKey DevicePublicKey { get; init; }
    public required byte[] Signature { get; init; }         // 64 bytes (covers everything above + body)
    public required VectorClock VectorClock { get; init; }
    public required IReadOnlyList<ProfileRecord> Profiles { get; init; }
    /// <summary>Raw bytes that the Ed25519 signature covers (everything before the 64-byte signature).</summary>
    public byte[]? SignedRegion { get; init; }
}

/// <summary>
/// A profile's serialized form: profile metadata + AEAD-encrypted payload.
/// The payload contains entries, folders, tags, templates per OKV_FORMAT.md §5.
/// WrappedDek is also encrypted (with KEK) and stored here.
/// </summary>
public sealed record ProfileRecord
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required ProfileColor Color { get; init; }
    public required ProfileSettings Settings { get; init; }
    public required WrappedKey WrappedDek { get; init; }
    /// <summary>AEAD-encrypted payload bytes (the body of §5 Profile Section).</summary>
    public required byte[] EncryptedPayload { get; init; }
    public required byte[] PayloadNonce { get; init; }
    public required byte[] PayloadTag { get; init; }
}

/// <summary>
/// Reads and writes .okv binary files per OKV_FORMAT.md §4-§6.
/// Implementations are responsible for:
///   - Parsing/writing the fixed magic header
///   - Verifying Ed25519 signature before returning a VaultRecord
///   - Serializing profiles + vector clock
/// </summary>
public interface IVaultFormat
{
    byte[] ComputeBuildHash();

    Task<VaultRecord> ReadAsync(string path, CancellationToken ct = default);
    Task WriteAsync(string path, VaultRecord record, DevicePrivateKey signingKey, CancellationToken ct = default);

    /// <summary>In-memory encode/decode without file I/O — used by tests and round-trip checks.</summary>
    byte[] Encode(VaultRecord record, DevicePrivateKey signingKey);
    VaultRecord Decode(byte[] bytes);
}

/// <summary>
/// .okv.dev seed file format (OKVD magic) per OKV_FORMAT.md §11.
/// Contains a plaintext Dev Master Key (no user password), so anyone with the
/// file can decrypt — by design, per PRD §5.5.3 / §11.4.
/// </summary>
public sealed record SeedRecord
{
    public required byte[] AppBuildHash { get; init; }      // 8 bytes
    public required Guid SeedUuid { get; init; }
    public required byte[] DevMasterKey { get; init; }       // 32B plaintext (NOT user password)
    public required byte[] DevSalt { get; init; }            // 32B (per OKV_FORMAT §11.2)
    public required bool StripMode { get; init; }            // true = sensitive fields were redacted
    public required IReadOnlyList<ProfileRecord> Profiles { get; init; }
    public required byte[] Signature { get; init; }          // 64B Ed25519
    public byte[]? SignedRegion { get; init; }              // bytes the signature covers
}

/// <summary>
/// Reads and writes .okv.dev seed files per OKV_FORMAT.md §11.
/// </summary>
public interface ISeedFormat
{
    byte[] ComputeBuildHash();

    Task<SeedRecord> ReadAsync(string path, CancellationToken ct = default);
    Task WriteAsync(string path, SeedRecord record, DevicePrivateKey signingKey, CancellationToken ct = default);

    byte[] Encode(SeedRecord record, DevicePrivateKey signingKey);
    SeedRecord Decode(byte[] bytes);
}
