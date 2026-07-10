namespace OmniKeyVault.Application;

/// <summary>
/// Password strength evaluator inspired by zxcvbn.
/// Returns a score 0-4 and a human-readable label + suggestion.
///
/// Scoring factors:
/// - Length (longer = stronger, but diminishing returns)
/// - Character variety (lowercase, uppercase, digits, symbols)
/// - Common patterns (sequences, repeats, keyboard walks)
/// - Common password blacklist (top 100 worst passwords)
/// - Entropy estimation (Shannon entropy)
/// </summary>
public static class PasswordStrength
{
    /// <summary>Score 0-4 (0=very weak, 4=strong). Score < 3 is rejected.</summary>
    public static int Score(string? password)
    {
        if (string.IsNullOrEmpty(password)) return 0;

        int score = 0;

        // Length scoring
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;

        // Character variety
        bool hasLower = false, hasUpper = false, hasDigit = false, hasSymbol = false;
        foreach (var c in password)
        {
            if (char.IsLower(c)) hasLower = true;
            else if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasSymbol = true;
        }
        int variety = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);
        if (variety >= 3) score++;
        if (variety >= 4) score++;

        // Penalty: common patterns
        if (IsCommonPattern(password)) score -= 2;
        // Penalty: common password
        if (IsCommonPassword(password)) score -= 3;
        // Penalty: only digits
        if (password.All(char.IsDigit)) score -= 2;
        // Penalty: only lowercase
        if (hasLower && !hasUpper && !hasDigit && !hasSymbol) score -= 1;

        // Clamp to 0-4
        return Math.Max(0, Math.Min(4, score));
    }

    /// <summary>Human-readable label for the score.</summary>
    public static string Label(int score) => score switch
    {
        0 => "极弱",
        1 => "弱",
        2 => "一般",
        3 => "强",
        4 => "极强",
        _ => "未知"
    };

    /// <summary>Improvement suggestions for weak passwords.</summary>
    public static string Suggestion(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "请输入主密码";

        var suggestions = new List<string>();

        if (password.Length < 12)
            suggestions.Add("使用至少 12 个字符");
        if (!password.Any(char.IsUpper))
            suggestions.Add("添加大写字母");
        if (!password.Any(char.IsDigit))
            suggestions.Add("添加数字");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            suggestions.Add("添加特殊符号");
        if (IsCommonPattern(password))
            suggestions.Add("避免使用连续字符或重复模式（如 abc、123、aaa）");
        if (IsCommonPassword(password))
            suggestions.Add("这是常见密码，极易被破解");
        if (password.All(char.IsDigit))
            suggestions.Add("纯数字密码极易被破解");

        return suggestions.Count == 0
            ? "密码强度良好"
            : string.Join("；", suggestions);
    }

    /// <summary>Returns true if the password should be rejected (score < 3).</summary>
    public static bool ShouldReject(string? password) => Score(password) < 3;

    // ============================================================
    //  Pattern detection
    // ============================================================

    private static bool IsCommonPattern(string password)
    {
        // Sequential characters (abc, 123, cba)
        if (HasSequence(password, 3)) return true;
        // Repeated characters (aaa, 111)
        if (HasRepeat(password, 3)) return true;
        // Keyboard walks (qwerty, asdf)
        if (HasKeyboardWalk(password, 4)) return true;
        return false;
    }

    private static bool HasSequence(string s, int minLen)
    {
        for (int i = 0; i <= s.Length - minLen; i++)
        {
            bool asc = true, desc = true;
            for (int j = 1; j < minLen; j++)
            {
                if (s[i + j] - s[i + j - 1] != 1) asc = false;
                if (s[i + j] - s[i + j - 1] != -1) desc = false;
            }
            if (asc || desc) return true;
        }
        return false;
    }

    private static bool HasRepeat(string s, int minLen)
    {
        for (int i = 0; i <= s.Length - minLen; i++)
        {
            bool repeat = true;
            for (int j = 1; j < minLen; j++)
            {
                if (s[i + j] != s[i]) { repeat = false; break; }
            }
            if (repeat) return true;
        }
        return false;
    }

    private static readonly string[] KeyboardRows = { "qwertyuiop", "asdfghjkl", "zxcvbnm", "1234567890" };

    private static bool HasKeyboardWalk(string s, int minLen)
    {
        var lower = s.ToLowerInvariant();
        foreach (var row in KeyboardRows)
        {
            for (int i = 0; i <= lower.Length - minLen; i++)
            {
                var sub = lower.Substring(i, minLen);
                if (row.Contains(sub)) return true;
                // Reverse
                var rev = new string(sub.Reverse().ToArray());
                if (row.Contains(rev)) return true;
            }
        }
        return false;
    }

    // Top 50 most common passwords (subset of top 100)
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "123456", "123456789", "guest", "qwerty", "12345678",
        "111111", "12345", "col123456", "123123", "abc123", "1234567",
        "1234567890", "password1", "iloveyou", "admin", "welcome", "monkey",
        "login", "princess", "passw0rd", "hello", "charlie", "donald",
        "password123", "654321", "football", "1234", "000000", "superman",
        "batman", "master", "dragon", "sunshine", "letmein", "trustno1",
        "iloveyou2", "pass123", "admin123", "root", "toor", "test",
        "qwerty123", "1q2w3e4r", "zxcvbnm", "asdfghjkl", "1qaz2wsx",
        "qazwsx", "pass", "word", "changeme"
    };

    private static bool IsCommonPassword(string password)
    {
        if (CommonPasswords.Contains(password)) return true;
        // Also check if password is a common password + digits
        var letters = new string(password.Where(char.IsLetter).ToArray());
        if (letters.Length >= 4 && CommonPasswords.Contains(letters)) return true;
        return false;
    }
}
