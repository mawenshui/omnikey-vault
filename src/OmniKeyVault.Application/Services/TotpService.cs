using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// Time-based One-Time Password (TOTP) per RFC 6238.
/// Used for <c>kind=totp_uri</c> Fields on Entries (PRD §5.8).
/// 6-digit codes, 30-second window, HMAC-SHA1 (per RFC 6238 default).
///
/// UI integration: GUI calls <see cref="GenerateCode"/> every 1s; the value
/// rotates when <see cref="GetRemainingSeconds"/> hits 0. Per PRD §5.8 the
/// auto-compute behavior is user-configurable (see ProfileSettings).
/// </summary>
[OmniKeyVaultService]
public sealed class TotpService
{
    public const int DefaultDigits = 6;
    public const int DefaultPeriodSeconds = 30;
    public const string DefaultAlgorithm = "SHA1";

    /// <summary>
    /// Computes the TOTP code for the given secret at the given Unix timestamp (seconds).
    /// </summary>
    public string GenerateCode(ReadOnlySpan<byte> secret, DateTimeOffset at, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
    {
        if (secret.IsEmpty) throw new ValidationException("TOTP secret is empty.");
        if (digits is < 6 or > 10) throw new ValidationException("TOTP digits must be in [6, 10].");
        if (periodSeconds <= 0) throw new ValidationException("TOTP period must be positive.");

        var counter = (ulong)(at.ToUnixTimeSeconds() / periodSeconds);
        return GenerateCodeAtCounter(secret, counter, digits);
    }

    /// <summary>
    /// Computes the TOTP code for the current wall-clock time. Uses <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    public string GenerateCode(ReadOnlySpan<byte> secret, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
        => GenerateCode(secret, DateTimeOffset.UtcNow, digits, periodSeconds);

    /// <summary>
    /// Returns the number of seconds remaining in the current TOTP window.
    /// UI typically shows this as a countdown ring per UI_UX_SPEC §4.4.2.
    /// </summary>
    public int GetRemainingSeconds(DateTimeOffset at, int periodSeconds = DefaultPeriodSeconds)
    {
        if (periodSeconds <= 0) throw new ValidationException("TOTP period must be positive.");
        var mod = at.ToUnixTimeSeconds() % periodSeconds;
        return periodSeconds - (int)mod;
    }

    /// <summary>
    /// Parses an otpauth:// URI (TOTP) and returns the secret bytes.
    /// Supported URI format: <c>otpauth://totp/{label}?secret={base32}&amp;period=30&amp;digits=6</c>
    /// </summary>
    public byte[] ParseSecretFromUri(string uri)
    {
        if (string.IsNullOrEmpty(uri)) throw new ValidationException("TOTP URI is empty.");
        if (!uri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("TOTP URI must start with 'otpauth://'.");

        // Quick tokenization: split on '?' to separate path and query, then '&' to separate query params.
        var qIdx = uri.IndexOf('?');
        if (qIdx < 0) throw new ValidationException("TOTP URI is missing query string.");
        var path = uri.AsSpan(0, qIdx);
        var query = uri.AsSpan(qIdx + 1);
        if (!path.Contains("totp", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Only TOTP URIs are supported (otpauth://totp/...).");

        var secretB32 = ExtractQueryParam(query, "secret");
        if (string.IsNullOrEmpty(secretB32))
            throw new ValidationException("TOTP URI is missing 'secret' parameter.");
        return Base32Decode(secretB32);
    }

    /// <summary>
    /// Builds an otpauth:// URI from a secret. Useful for QR generation in the GUI.
    /// </summary>
    public string BuildUri(string label, ReadOnlySpan<byte> secret, string? issuer = null, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
    {
        var b32 = Base32Encode(secret);
        var labelEncoded = Uri.EscapeDataString(label);
        var sb = new StringBuilder();
        sb.Append("otpauth://totp/").Append(labelEncoded);
        sb.Append("?secret=").Append(b32);
        sb.Append("&algorithm=").Append(DefaultAlgorithm);
        sb.Append("&digits=").Append(digits);
        sb.Append("&period=").Append(periodSeconds);
        if (!string.IsNullOrEmpty(issuer))
            sb.Append("&issuer=").Append(Uri.EscapeDataString(issuer));
        return sb.ToString();
    }

    /// <summary>
    /// Encodes bytes as RFC 4648 base32 (uppercase, no padding).
    /// </summary>
    public string Base32Encode(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (data.IsEmpty) return string.Empty;
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        ulong buffer = 0;
        int bitsInBuffer = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                var idx = (int)((buffer >> bitsInBuffer) & 0x1F);
                sb.Append(alphabet[idx]);
            }
        }
        if (bitsInBuffer > 0)
        {
            var idx = (int)((buffer << (5 - bitsInBuffer)) & 0x1F);
            sb.Append(alphabet[idx]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Decodes an RFC 4648 base32 string (uppercase, with or without padding).
    /// </summary>
    public byte[] Base32Decode(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var lookup = new int[128];
        for (int i = 0; i < 128; i++) lookup[i] = -1;
        for (int i = 0; i < alphabet.Length; i++) lookup[alphabet[i]] = i;
        // Lowercase also accepted.
        for (int i = 0; i < alphabet.Length; i++) lookup[char.ToLowerInvariant(alphabet[i])] = i;

        // Strip padding.
        s = s.TrimEnd('=').Replace(" ", "").Replace("-", "");
        if (s.Length == 0) return Array.Empty<byte>();

        var bytes = new List<byte>(s.Length * 5 / 8 + 1);
        ulong buffer = 0;
        int bitsInBuffer = 0;
        foreach (var c in s)
        {
            if (c >= 128 || lookup[c] < 0)
                throw new ValidationException($"Invalid base32 character: '{c}'.");
            buffer = (buffer << 5) | (uint)lookup[c];
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((buffer >> bitsInBuffer) & 0xFF));
            }
        }
        return bytes.ToArray();
    }

    // ---- internals ----

    private static string GenerateCodeAtCounter(ReadOnlySpan<byte> secret, ulong counter, int digits)
    {
        // RFC 6238 §5.3: T = counter as 8-byte big-endian
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        // HMAC-SHA1
        Span<byte> hash = stackalloc byte[20];
        using (var hmac = new HMACSHA1(secret.ToArray()))
        {
            if (!hmac.TryComputeHash(counterBytes, hash, out var written) || written != 20)
                throw new CryptoException("HMAC-SHA1 compute failed.");
        }

        // Dynamic truncation (RFC 4226 §5.3)
        var offset = hash[19] & 0x0F;
        var binary = ((uint)(hash[offset] & 0x7F) << 24)
                   | ((uint)(hash[offset + 1] & 0xFF) << 16)
                   | ((uint)(hash[offset + 2] & 0xFF) << 8)
                   |  (uint)(hash[offset + 3] & 0xFF);

        var mod = (int)Math.Pow(10, digits);
        var code = binary % mod;
        return code.ToString().PadLeft(digits, '0');
    }

    private static string ExtractQueryParam(ReadOnlySpan<char> query, string key)
    {
        // query is the substring after '?' (no leading '?'). Format: k1=v1&k2=v2&...
        while (!query.IsEmpty)
        {
            var amp = query.IndexOf('&');
            ReadOnlySpan<char> pair = amp < 0 ? query : query.Slice(0, amp);
            var eq = pair.IndexOf('=');
            if (eq > 0)
            {
                var k = pair.Slice(0, eq);
                if (k.Equals(key.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    var v = pair.Slice(eq + 1);
                    return Uri.UnescapeDataString(v.ToString());
                }
            }
            if (amp < 0) break;
            query = query.Slice(amp + 1);
        }
        return string.Empty;
    }
}
