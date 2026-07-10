using System.Security.Cryptography;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using SodiumCore = Sodium.SodiumCore;
using PasswordHash = Sodium.PasswordHash;
using SecretAead = Sodium.SecretAeadXChaCha20Poly1305;
using PublicKeyAuth = Sodium.PublicKeyAuth;
using SecretKeyAuth = Sodium.SecretKeyAuth;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// libsodium-backed crypto provider (Sodium.Core wrapper) per ARCHITECTURE.md ADR-002.
/// Implements SECURITY.md §3 cryptography suite:
///   - Argon2id (RFC 9106) with non-default 256 MiB memory cost (PRD §4.4)
///   - XChaCha20-Poly1305 (24-byte nonce) AEAD
///   - Ed25519 device signatures (RFC 8032)
///   - OS CSPRNG via SodiumCore.GetRandomBytes (libsodium randombytes_buf)
/// All methods accept byte spans (INV-01); comparisons use FixedTimeEquals (INV-07).
/// </summary>
public sealed class SodiumCryptoProvider : ICryptoProvider
{
    private const string KekInfoString = "okv-kek-v1";
    private static readonly byte[] KekInfo = System.Text.Encoding.UTF8.GetBytes(KekInfoString);

    public SodiumCryptoProvider()
    {
        // SodiumCore.Init() must be called once per process before any sodium operation.
        try { SodiumCore.Init(); } catch { /* already initialized */ }
    }

    public MasterKey DeriveMasterKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Argon2Params args)
    {
        // libsodium crypto_pwhash Argon2id: opsLimit is iteration count, memLimit is bytes.
        // We use the explicit (opsLimit, memLimit) overload rather than the Strength enum
        // so we can set non-default parameters per PRD §4.4.
        var key = PasswordHash.ArgonHashBinary(
            password.ToArray(),
            salt.ToArray(),
            opsLimit: (long)args.Time,
            memLimit: (int)args.Memory,
            outputLength: (long)args.KeyLength,
            alg: PasswordHash.ArgonAlgorithm.Argon_2ID13);
        return MasterKey.From(key);
    }

    public bool VerifyMasterKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Argon2Params args, ReadOnlySpan<byte> verifyTag)
    {
        // Constant-time verification per INV-07. Derive MK -> KEK -> recompute verify tag.
        using var mk = DeriveMasterKey(password, salt, args);
        using var kek = DeriveKek(mk, KekInfo, salt);
        var computed = ComputeVerifyTag(kek, Array.Empty<byte>());
        return FixedTimeEquals(computed, verifyTag);
    }

    public KeyEncryptionKey DeriveKek(MasterKey mk, ReadOnlySpan<byte> info, ReadOnlySpan<byte> salt)
    {
        // HKDF-SHA256 per SECURITY.md §4.1: Extract(salt, MK) -> PRK, then Expand(PRK, info, 32).
        var saltArr = salt.ToArray();
        var mkArr = mk.Span.ToArray();
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, mkArr, saltArr);
        CryptographicOperations.ZeroMemory(mkArr);
        var infoArr = info.IsEmpty ? KekInfo : info.ToArray();
        var kekBytes = HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, infoArr);
        CryptographicOperations.ZeroMemory(prk);
        return KeyEncryptionKey.From(kekBytes);
    }

    public WrappedKey WrapKey(KeyEncryptionKey kek, DataEncryptionKey dek)
    {
        // XChaCha20-Poly1305 KWrap per SECURITY.md §3.1.
        // Wrap = encrypt DEK with KEK, AAD = "okv-kwrap-v1".
        var aad = System.Text.Encoding.UTF8.GetBytes("okv-kwrap-v1");
        var nonce = SecretAead.GenerateNonce();
        var ct = SecretAead.Encrypt(dek.Span.ToArray(), nonce, kek.Span.ToArray(), aad);
        // SecretAead.Encrypt returns ct+tag concatenated (tag is last 16 bytes).
        var tag = new byte[16];
        var ctOnly = new byte[ct.Length - 16];
        Buffer.BlockCopy(ct, 0, ctOnly, 0, ctOnly.Length);
        Buffer.BlockCopy(ct, ct.Length - 16, tag, 0, 16);
        return new WrappedKey(nonce, ctOnly, tag);
    }

    public DataEncryptionKey UnwrapKey(KeyEncryptionKey kek, WrappedKey wrapped)
    {
        var aad = System.Text.Encoding.UTF8.GetBytes("okv-kwrap-v1");
        var ctWithTag = new byte[wrapped.Ciphertext.Length + wrapped.Tag.Length];
        Buffer.BlockCopy(wrapped.Ciphertext, 0, ctWithTag, 0, wrapped.Ciphertext.Length);
        Buffer.BlockCopy(wrapped.Tag, 0, ctWithTag, wrapped.Ciphertext.Length, wrapped.Tag.Length);
        try
        {
            var dekBytes = SecretAead.Decrypt(ctWithTag, wrapped.Nonce, kek.Span.ToArray(), aad);
            return DataEncryptionKey.From(dekBytes);
        }
        catch (Exception ex) when (ex is not CryptoException)
        {
            throw new CryptoException("DEK unwrap failed (KEK incorrect or wrapped key corrupted).", ex);
        }
    }

    public EncryptedPayload Encrypt(DataEncryptionKey dek, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad)
    {
        var nonce = SecretAead.GenerateNonce();
        var ct = SecretAead.Encrypt(plaintext.ToArray(), nonce, dek.Span.ToArray(), aad.ToArray());
        var tag = new byte[16];
        var ctOnly = new byte[ct.Length - 16];
        Buffer.BlockCopy(ct, 0, ctOnly, 0, ctOnly.Length);
        Buffer.BlockCopy(ct, ct.Length - 16, tag, 0, 16);
        return new EncryptedPayload(nonce, ctOnly, tag, aad.ToArray());
    }

    public byte[] Decrypt(DataEncryptionKey dek, in EncryptedPayload payload, ReadOnlySpan<byte> aad)
    {
        var ctWithTag = new byte[payload.Ciphertext.Length + payload.Tag.Length];
        Buffer.BlockCopy(payload.Ciphertext, 0, ctWithTag, 0, payload.Ciphertext.Length);
        Buffer.BlockCopy(payload.Tag, 0, ctWithTag, payload.Ciphertext.Length, payload.Tag.Length);
        try
        {
            return SecretAead.Decrypt(ctWithTag, payload.Nonce, dek.Span.ToArray(), aad.ToArray());
        }
        catch (Exception ex) when (ex is not CryptoException)
        {
            // INV-08: AEAD failure must not return partial plaintext (libsodium guarantees this).
            throw new CryptoException("AEAD authentication failed.", ex);
        }
    }

    public byte[] ComputeVerifyTag(KeyEncryptionKey kek, ReadOnlySpan<byte> context)
    {
        // Per OKV_FORMAT.md §4.2 — MAC of context under KEK.
        // HMAC-SHA256(KEK, "okv-verify-v1" || context) — 32 bytes, fits the fixed header slot.
        var prefix = System.Text.Encoding.UTF8.GetBytes("okv-verify-v1");
        var verifyInput = new byte[prefix.Length + context.Length];
        Buffer.BlockCopy(prefix, 0, verifyInput, 0, prefix.Length);
        if (context.Length > 0)
        {
            var ctxArr = context.ToArray();
            Buffer.BlockCopy(ctxArr, 0, verifyInput, prefix.Length, ctxArr.Length);
            CryptographicOperations.ZeroMemory(ctxArr);
        }
        return SecretKeyAuth.SignHmacSha256(verifyInput, kek.Span.ToArray());
    }

    public bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => CryptographicOperations.FixedTimeEquals(a, b);

    public DeviceKeyPair GenerateDeviceKeyPair()
    {
        var kp = PublicKeyAuth.GenerateKeyPair();
        return new DeviceKeyPair(new DevicePublicKey(kp.PublicKey), DevicePrivateKey.From(kp.PrivateKey));
    }

    public byte[] Sign(DevicePrivateKey key, ReadOnlySpan<byte> data)
        => PublicKeyAuth.SignDetached(data.ToArray(), key.Span.ToArray());

    public bool Verify(DevicePublicKey key, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        // P4-T3: Only swallow CryptographicException — other exceptions
        // (OutOfMemoryException, DllNotFoundException, SEHException, etc.)
        // indicate systemic faults that must propagate, not be masked as
        // "signature invalid" (which could hide a security-critical failure).
        try
        {
            return PublicKeyAuth.VerifyDetached(signature.ToArray(), data.ToArray(), key.Bytes);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public byte[] RandomBytes(int count) => SodiumCore.GetRandomBytes(count);

    public Guid NewUuidV7()
    {
        // UUIDv7: 48-bit unix-ms timestamp + 12-bit rand_a (ver=7) + 14-bit rand_b (var=10).
        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> b = stackalloc byte[16];
        for (int i = 5; i >= 0; i--)
        {
            b[i] = (byte)(ms & 0xFF);
            ms >>= 8;
        }
        var rand = RandomBytes(2);
        b[6] = (byte)((rand[0] & 0x0F) | 0x70);
        b[7] = rand[1];
        var rand2 = RandomBytes(8);
        rand2.CopyTo(b.Slice(8));
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b, bigEndian: true);
    }

    public void Zero(Span<byte> buffer) => CryptographicOperations.ZeroMemory(buffer);
}
