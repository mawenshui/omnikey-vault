namespace OmniKeyVault.Domain;

/// <summary>
/// Argon2id KDF parameters per SECURITY.md §3.2.1.
/// Default: t=3, m=256 MiB, p=4 — non-default to make hashcat
/// preset modes fail (PRD §4.4 design philosophy).
/// </summary>
public sealed record Argon2Params
{
    /// <summary>Iteration count. RFC 9106 recommends 1-3; we use 3.</summary>
    public uint Time { get; init; } = 3;

    /// <summary>Memory cost in bytes. Default 256 MiB (268435456).</summary>
    public uint Memory { get; init; } = 256 * 1024 * 1024;

    /// <summary>Parallelism (lanes). Default 4.</summary>
    public byte Parallelism { get; init; } = 4;

    /// <summary>Output key length in bytes. Default 32 (256-bit master key).</summary>
    public uint KeyLength { get; init; } = 32;

    /// <summary>Salt length in bytes. Default 16 — matches libsodium crypto_pwhash_SALTBYTES.</summary>
    /// <remarks>
    /// v0.1 MVP deviation from OKV_FORMAT.md §4.1 (which specifies 32B):
    /// libsodium's crypto_pwhash API enforces 16B salt. The header slot is
    /// 32B (per spec); the first 16B are the KDF salt, the remaining 16B are
    /// reserved for future use (e.g., PQ upgrade pepper). See test report §A.1.
    /// </remarks>
    public uint SaltLength { get; init; } = 16;

    public static Argon2Params Default { get; } = new();

    /// <summary>
    /// Returns a params instance with reduced memory cost for tests / dev mode.
    /// NEVER use this in production — INV-06 requires m >= 256 MiB.
    /// libsodium crypto_pwhash enforces opsLimit >= 3 (crypto_pwhash_OPSLIMIT_MIN).
    /// </summary>
    public static Argon2Params ForTests(uint memoryBytes = 1 * 1024 * 1024)
        => new() { Time = 3, Memory = memoryBytes, Parallelism = 1, KeyLength = 32 };
}
