using System.Security.Cryptography;
using System.Text;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// P11-T1: Shared vault crypto helpers, extracted from VaultService/SeedExporter/
/// SeedImporter/SyncService to eliminate duplication (Phase 11 de-duplication).
/// </summary>
public static class VaultCryptoHelpers
{
    /// <summary>Builds the 32-byte AAD that binds a profile payload to its
    /// vault/seed UUID + profile ID (SECURITY.md §3.3 / INV-AAD).
    /// AAD = uuid(16) + profile_id(16).</summary>
    public static byte[] BuildProfileAad(Guid vaultUuid, Guid profileId)
    {
        var aad = new byte[32];
        Buffer.BlockCopy(vaultUuid.ToByteArray(), 0, aad, 0, 16);
        Buffer.BlockCopy(profileId.ToByteArray(), 0, aad, 16, 16);
        return aad;
    }

    /// <summary>Collects all unique tags from a list of entries, sorted alphabetically.</summary>
    public static List<string> CollectTags(IReadOnlyList<Entry> entries)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
            foreach (var t in e.Tags)
                set.Add(t);
        return set.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }
}


/// <summary>
/// P8-T7: Field masking utility extracted from MainWindow.axaml.cs.
/// Provides consistent masking across CLI and GUI.
/// </summary>
public static class FieldMasking
{
    /// <summary>Masks a decoded string value for display. Uses custom mask if provided,
    /// otherwise generates one per PLATFORM_TEMPLATES.md §2.4 rules.</summary>
    public static string MaskValue(string value, string? customMask = null)
    {
        if (!string.IsNullOrEmpty(customMask)) return customMask;
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= 8) return "••••••••";
        return value.Substring(0, 3) + "•••••••••••" + value.Substring(value.Length - 4);
    }

    /// <summary>Masks a byte[] field value for display.</summary>
    public static string MaskValue(byte[] value, string? customMask = null)
        => MaskValue(FieldCodec.Decode(value), customMask);
}

/// <summary>
/// P8-T6: OTP auth URI parsing + Base32 decoding, extracted from MainWindow.axaml.cs.
/// </summary>
public static class OtpAuthUtils
{
    /// <summary>Parses an otpauth:// URI and extracts the secret as raw bytes.</summary>
    public static byte[]? ParseSecretFromUri(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (!uri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) return null;

        // Extract secret parameter
        var secretIdx = uri.IndexOf("secret=", StringComparison.OrdinalIgnoreCase);
        if (secretIdx < 0) return null;
        var start = secretIdx + 7;
        var end = uri.IndexOf('&', start);
        if (end < 0) end = uri.Length;
        var secret = uri.Substring(start, end - start);

        return Base32Decode(secret);
    }

    /// <summary>Decodes a Base32 string (RFC 4648) to raw bytes.</summary>
    public static byte[] Base32Decode(string input)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();

        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();

        var bytes = new List<byte>();
        int buffer = 0;
        int bitsLeft = 0;

        foreach (var c in input)
        {
            var idx = alphabet.IndexOf(c);
            if (idx < 0) continue;
            buffer = (buffer << 5) | idx;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return bytes.ToArray();
    }
}

/// <summary>
/// P11-T5: Centralized crypto context strings to eliminate magic strings.
/// </summary>
public static class CryptoConstants
{
    public const string KekContext = "okv-kek-v1";
    public const string KwrapContext = "okv-kwrap-v1";
    public const string VerifyContext = "okv-verify-v1";
    public const string SeedKekContext = "okv-seed-kek-v1";
}
