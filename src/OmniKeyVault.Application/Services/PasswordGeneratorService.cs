using System.Security.Cryptography;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: Cryptographically secure password generator.
/// Uses <see cref="RandomNumberGenerator"/> for entropy. Supports configurable
/// length, character sets, and exclusion of ambiguous characters.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class PasswordGeneratorService
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*()_+-=[]{}|;:,.<>?";
    private const string UppercaseAll = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseAll = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitsAll = "0123456789";
    private const string AmbiguousChars = "Il1O0oS5B8G6";

    /// <summary>Generates a random password with the given options.</summary>
    public string Generate(int length = 20, bool useUppercase = true, bool useLowercase = true,
        bool useDigits = true, bool useSymbols = true, bool excludeAmbiguous = true)
    {
        if (length < 4) length = 4;
        if (length > 128) length = 128;

        var pools = new List<string>();
        if (useUppercase) pools.Add(excludeAmbiguous ? Uppercase : UppercaseAll);
        if (useLowercase) pools.Add(excludeAmbiguous ? Lowercase : LowercaseAll);
        if (useDigits) pools.Add(excludeAmbiguous ? Digits : DigitsAll);
        if (useSymbols) pools.Add(Symbols);

        if (pools.Count == 0) pools.Add(Lowercase);

        var allChars = string.Concat(pools);
        var result = new char[length];

        // Ensure at least one character from each selected pool
        var pos = 0;
        foreach (var pool in pools)
        {
            if (pos >= length) break;
            result[pos++] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }

        // Fill remaining with random characters from all pools
        for (var i = pos; i < length; i++)
        {
            result[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];
        }

        // Fisher-Yates shuffle
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return new string(result);
    }

    /// <summary>Generates a passphrase from words (simpler but longer).</summary>
    public string GeneratePassphrase(int wordCount = 4, string separator = "-")
    {
        var words = new[]
        {
            "apple", "brave", "cloud", "dance", "eagle", "flame", "grace", "haven",
            "ivory", "jungle", "kneel", "lemon", "maple", "noble", "ocean", "pearl",
            "quest", "raven", "storm", "tiger", "ultra", "vivid", "whale", "xenon",
            "yacht", "zebra", "alpha", "blaze", "coral", "delta", "ember", "frost",
            "globe", "honor", "ideal", "jewel", "knife", "lotus", "magic", "north",
            "orbit", "prism", "quiet", "royal", "spark", "trace", "unity", "vault",
            "water", "youth", "amber", "bloom", "charm", "dream", "elite", "forge",
            "glide", "heart", "index", "jolly", "karma", "lunar", "march", "novel",
            "opera", "plume", "quartz", "ridge", "shade", "tower", "urban", "voice",
            "wheat", "xray", "yield", "zonal", "arctic", "breeze", "crown", "dawn",
            "echo", "fable", "gem", "halo", "iris", "jazz", "kite", "leaf"
        };
        var parts = new string[wordCount];
        for (var i = 0; i < wordCount; i++)
        {
            parts[i] = words[RandomNumberGenerator.GetInt32(words.Length)];
        }
        return string.Join(separator, parts);
    }

    /// <summary>Estimates password strength (0-4 scale, like zxcvbn).</summary>
    public static int EstimateStrength(string password)
    {
        if (string.IsNullOrEmpty(password)) return 0;
        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;
        var hasUpper = password.Any(char.IsUpper);
        var hasLower = password.Any(char.IsLower);
        var hasDigit = password.Any(char.IsDigit);
        var hasSymbol = password.Any(c => !char.IsLetterOrDigit(c));
        var variety = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);
        if (variety >= 3) score++;
        if (variety >= 4 && password.Length >= 20) score = 4;
        return Math.Min(score, 4);
    }

    /// <summary>Returns a human-readable strength label.</summary>
    public static string StrengthLabel(int score) => score switch
    {
        0 => "极弱",
        1 => "弱",
        2 => "中等",
        3 => "强",
        4 => "极强",
        _ => "未知"
    };
}
