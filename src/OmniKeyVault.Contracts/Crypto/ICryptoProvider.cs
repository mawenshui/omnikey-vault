using OmniKeyVault.Domain;

namespace OmniKeyVault.Contracts;

/// <summary>
/// Cryptography provider interface per ARCHITECTURE.md §6.1.
/// All methods accept byte spans (INV-01: never accept string).
/// Implementations must:
///   - Generate unique 24B nonce per Encrypt call (INV-02)
///   - Use OS CSPRNG for randomness (SECURITY.md §3.1)
///   - Provide constant-time comparison for verify operations (INV-07)
/// </summary>
public interface ICryptoProvider
{
    // ---- KDF ----
    MasterKey DeriveMasterKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Argon2Params args);
    bool VerifyMasterKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Argon2Params args, ReadOnlySpan<byte> verifyTag);

    /// <summary>v1.6: Double Argon2id key stretching. Derives MK1 from the user
    /// password (round 1, full 256 MiB), then derives MK2 from MK1 (round 2,
    /// reduced 64 MiB). Doubles the per-guess cost for offline brute-force
    /// attackers who have both the .okv file and the source code.</summary>
    MasterKey DeriveMasterKeyV2(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt1, ReadOnlySpan<byte> salt2, Argon2Params argsRound1, Argon2Params argsRound2);

    // ---- Key derivation ----
    KeyEncryptionKey DeriveKek(MasterKey mk, ReadOnlySpan<byte> info, ReadOnlySpan<byte> salt);

    // ---- Key wrapping (XChaCha20-Poly1305 KWrap per SECURITY.md §3.1) ----
    WrappedKey WrapKey(KeyEncryptionKey kek, DataEncryptionKey dek);
    DataEncryptionKey UnwrapKey(KeyEncryptionKey kek, WrappedKey wrapped);

    // ---- AEAD (XChaCha20-Poly1305) ----
    EncryptedPayload Encrypt(DataEncryptionKey dek, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad);
    byte[] Decrypt(DataEncryptionKey dek, in EncryptedPayload payload, ReadOnlySpan<byte> aad);

    /// <summary>Compute a 32-byte verify tag for the given KEK (used in .okv header §4.2).</summary>
    byte[] ComputeVerifyTag(KeyEncryptionKey kek, ReadOnlySpan<byte> context);

    /// <summary>Constant-time equality check (INV-07).</summary>
    bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b);

    // ---- Ed25519 device signing ----
    DeviceKeyPair GenerateDeviceKeyPair();
    byte[] Sign(DevicePrivateKey key, ReadOnlySpan<byte> data);
    bool Verify(DevicePublicKey key, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature);

    // ---- Random ----
    byte[] RandomBytes(int count);
    Guid NewUuidV7();

    // ---- Memory safety ----
    void Zero(Span<byte> buffer);
}
