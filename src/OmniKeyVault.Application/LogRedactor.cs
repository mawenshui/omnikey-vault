using System.Text.RegularExpressions;

namespace OmniKeyVault.Application;

/// <summary>
/// Log redaction filter that masks sensitive field values in log messages.
/// SECURITY.md §8.2: "日志上下文禁止包含明文凭据、密钥、主密码".
///
/// Recognizes common sensitive field name patterns (password, secret,
/// api_key, token, private_key, dek, kek, mk, recovery_key, verify_tag,
/// nonce, tag, signature, wrapped_*) and replaces their associated values
/// with '***'.
/// </summary>
public static class LogRedactor
{
    /// <summary>
    /// Field name prefixes/keywords that indicate a sensitive value.
    /// Match is case-insensitive, checked with Contains.
    /// </summary>
    private static readonly string[] SensitiveKeywords =
    {
        "password", "passwd", "pwd", "secret", "api_key", "apikey",
        "token", "access_token", "refresh_token", "private_key", "privatekey",
        "dek", "kek", "master_key", "masterkey", "mk",
        "recovery_key", "recoverykey", "verify_tag", "verifytag",
        "nonce", "tag", "signature", "wrapped_",
        "otp_secret", "totp_secret", "totp_uri",
        "ssh_key", "privatekey", "credential"
    };

    /// <summary>
    /// Known secret value prefixes that should be masked anywhere they appear.
    /// </summary>
    private static readonly string[] SecretValuePrefixes =
    {
        "sk-", "ghp_", "gho_", "ghu_", "ghs_", "ghr_",
        "AKIA", "AIza",
        "xoxb-", "xoxp-",
        "-----BEGIN"
    };

    // Pre-compiled regex: matches "key=value" or "key: value" patterns
    // where key contains a sensitive keyword.
    private static readonly Regex SensitiveFieldRegex = new(
        @"(?i)(?<key>(?:password|passwd|pwd|secret|api[_-]?key|token|access[_-]?token|refresh[_-]?token|private[_-]?key|dek|kek|master[_-]?key|mk|recovery[_-]?key|verify[_-]?tag|nonce|tag|signature|wrapped_.+?|otp[_-]?secret|totp[_-]?secret|totp[_-]?uri|ssh[_-]?key|credential))\s*[:=]\s*(?<value>[^\s,;\]}]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Redacts sensitive values from a log message.
    /// Replaces values associated with sensitive field names with "***".
    /// Also masks known secret value prefixes (sk-, ghp_, AKIA, etc.).
    /// </summary>
    public static string Redact(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? string.Empty;

        var result = SensitiveFieldRegex.Replace(message, "${key}=***");

        // Also mask known secret value prefixes
        foreach (var prefix in SecretValuePrefixes)
        {
            if (result.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Mask anything that starts with a known prefix up to the next whitespace or delimiter
                var pattern = Regex.Escape(prefix) + @"[A-Za-z0-9_\-]+";
                result = Regex.Replace(result, pattern, prefix + "***",
                    RegexOptions.IgnoreCase);
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if a field name is considered sensitive.
    /// </summary>
    public static bool IsSensitiveFieldName(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return false;
        var lower = fieldName.ToLowerInvariant();
        foreach (var keyword in SensitiveKeywords)
        {
            if (lower.Contains(keyword)) return true;
        }
        return false;
    }
}
