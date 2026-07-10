using System.Security.Cryptography;
using System.Text;

namespace OmniKeyVault.Application;

/// <summary>
/// P6-T2: UTF-8 encode/decode helper for Field.Value (byte[]).
/// Centralizes all string ↔ byte[] conversions so that the encoding is
/// consistent across the codebase and sensitive values can be zeroed.
/// </summary>
public static class FieldCodec
{
    /// <summary>Encode a string to UTF-8 bytes.</summary>
    public static byte[] Encode(string s)
        => s == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(s);

    /// <summary>Decode UTF-8 bytes to a string.</summary>
    public static string Decode(byte[]? b)
        => b == null || b.Length == 0 ? string.Empty : Encoding.UTF8.GetString(b);

    /// <summary>Decode UTF-8 bytes to a string (span overload).</summary>
    public static string Decode(ReadOnlySpan<byte> b)
        => b.IsEmpty ? string.Empty : Encoding.UTF8.GetString(b);

    /// <summary>Zero a byte array (for sensitive field values).</summary>
    public static void Zero(byte[]? bytes)
    {
        if (bytes != null && bytes.Length > 0)
            CryptographicOperations.ZeroMemory(bytes);
    }
}
