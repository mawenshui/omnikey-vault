using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: Credential leak detection using the HaveIBeenPwned API (k-anonymity mode).
/// Only sends the first 5 characters of the SHA-1 hash of the password — the
/// full password hash never leaves the device. The response contains a list of
/// hash suffixes with breach counts; we match locally.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class CredentialLeakService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string ApiBaseUrl = "https://api.pwnedpasswords.com/range/";

    public CredentialLeakService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { { "User-Agent", "OmniKeyVault/2.0" } }
        };
    }

    /// <summary>Checks if a password has been found in known data breaches.
    /// Returns the number of times it was found (0 = not breached).</summary>
    public async Task<int> CheckPasswordAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password)) return 0;

        // Compute SHA-1 hash (uppercase hex)
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(password));
        var hashHex = Convert.ToHexString(hashBytes); // uppercase

        var prefix = hashHex[..5];
        var suffix = hashHex[5..];

        // Fetch all hash suffixes matching the 5-char prefix
        var response = await _httpClient.GetStringAsync($"{ApiBaseUrl}{prefix}", ct);
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;
            var hashSuffix = line[..colonIdx].Trim();
            var countStr = line[(colonIdx + 1)..].Trim();

            if (string.Equals(hashSuffix, suffix, StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(countStr, out var count) ? count : 0;
            }
        }
        return 0;
    }

    /// <summary>Checks multiple passwords at once. Returns a dictionary of
    /// password → breach count.</summary>
    public async Task<Dictionary<string, int>> CheckPasswordsAsync(IEnumerable<string> passwords, CancellationToken ct = default)
    {
        var result = new Dictionary<string, int>();
        foreach (var password in passwords.Distinct())
        {
            result[password] = await CheckPasswordAsync(password, ct);
        }
        return result;
    }

    public void Dispose() => _httpClient.Dispose();
}
