using System.Security.Cryptography;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Contracts;

/// <summary>
/// Opaque handle to a 32-byte master key (MK). Disposing zeroes memory.
/// SECURITY.md §6.2: SecureKey pattern — never use raw byte[] for keys.
/// </summary>
public abstract class SecureKey : IDisposable
{
    private readonly byte[] _bytes;
    private bool _disposed;

    protected SecureKey(byte[] bytes)
    {
        _bytes = bytes;
    }

    /// <summary>Raw key bytes. Valid until Dispose.</summary>
    public ReadOnlySpan<byte> Span => _bytes.AsSpan();

    /// <summary>Length in bytes.</summary>
    public int Length => _bytes.Length;

    /// <summary>Create a copy as a new array (caller must clear).</summary>
    public byte[] ToArray()
    {
        var copy = new byte[_bytes.Length];
        Array.Copy(_bytes, copy, _bytes.Length);
        return copy;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_bytes);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~SecureKey()
    {
        Dispose();
    }
}

public sealed class MasterKey : SecureKey
{
    internal MasterKey(byte[] bytes) : base(bytes) { }
    public static MasterKey From(byte[] bytes) => new(bytes);
}

public sealed class KeyEncryptionKey : SecureKey
{
    internal KeyEncryptionKey(byte[] bytes) : base(bytes) { }
    public static KeyEncryptionKey From(byte[] bytes) => new(bytes);
}

public sealed class DataEncryptionKey : SecureKey
{
    internal DataEncryptionKey(byte[] bytes) : base(bytes) { }
    public static DataEncryptionKey From(byte[] bytes) => new(bytes);
}

public sealed class DevicePrivateKey : SecureKey
{
    internal DevicePrivateKey(byte[] bytes) : base(bytes) { }
    public static DevicePrivateKey From(byte[] bytes) => new(bytes);
}

public sealed record DevicePublicKey(byte[] Bytes);
public sealed record DeviceKeyPair(DevicePublicKey PublicKey, DevicePrivateKey PrivateKey);

/// <summary>
/// Wrapped (encrypted) key blob — 24B nonce + N ct + 16B tag.
/// Used to persist DEKs in the .okv header (PRD §4.3).
/// </summary>
public sealed record WrappedKey(byte[] Nonce, byte[] Ciphertext, byte[] Tag)
{
    public byte[] ToArray()
    {
        var r = new byte[Nonce.Length + Ciphertext.Length + Tag.Length];
        Buffer.BlockCopy(Nonce, 0, r, 0, Nonce.Length);
        Buffer.BlockCopy(Ciphertext, 0, r, Nonce.Length, Ciphertext.Length);
        Buffer.BlockCopy(Tag, 0, r, Nonce.Length + Ciphertext.Length, Tag.Length);
        return r;
    }

    public static WrappedKey FromArray(byte[] blob, int nonceLen = 24, int tagLen = 16)
    {
        if (blob.Length < nonceLen + tagLen)
            throw new ArgumentException("WrappedKey blob too short.", nameof(blob));
        var nonce = blob.AsSpan(0, nonceLen).ToArray();
        var ct = blob.AsSpan(nonceLen, blob.Length - nonceLen - tagLen).ToArray();
        var tag = blob.AsSpan(blob.Length - tagLen, tagLen).ToArray();
        return new WrappedKey(nonce, ct, tag);
    }
}

/// <summary>
/// AEAD-encrypted payload — 24B nonce + N ct + 16B tag (XChaCha20-Poly1305).
/// </summary>
public sealed record EncryptedPayload(byte[] Nonce, byte[] Ciphertext, byte[] Tag, byte[] Aad)
{
    public byte[] NonceTagCt
    {
        get
        {
            var r = new byte[Nonce.Length + Tag.Length + Ciphertext.Length];
            Buffer.BlockCopy(Nonce, 0, r, 0, Nonce.Length);
            Buffer.BlockCopy(Tag, 0, r, Nonce.Length, Tag.Length);
            Buffer.BlockCopy(Ciphertext, 0, r, Nonce.Length + Tag.Length, Ciphertext.Length);
            return r;
        }
    }
}
