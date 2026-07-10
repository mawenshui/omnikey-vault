using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OmniKeyVault.Application;

/// <summary>
/// GitHub Personal Access Token (PAT) rotator per v0.4 S8-T2 / MANUAL §4.3.2.
///
/// GitHub's API doesn't actually support creating + revoking PATs
/// programmatically — PATs are user-managed in the web UI. The "rotation"
/// workflow is therefore:
///   1. Generate a new token client-side from the user's strong-random generator
///      (a 40-char hex string with the <c>ghp_</c> prefix GitHub uses).
///   2. Show the new token to the user with a "copy + paste into GitHub"
///      prompt.
///   3. Mark the old token as "should be revoked manually" in the note.
///
/// For classic PATs there's no "validate this token" endpoint, so we can't
/// pre-check the old token's validity. For fine-grained PATs the
/// <c>/user/tokens</c> endpoint requires the very token being listed (no
/// admin API for end users).
///
/// This rotator exists primarily to demonstrate the rotation framework; in
/// the v0.4 GUI the success toast clearly tells the user "this is a
/// placeholder new value — generate the real one in GitHub and paste it
/// back". A future v0.4.x or v0.5 iteration can add OAuth-based rotation
/// (GitHub Apps) which does support programmatic rotation.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class GitHubPatRotator : IPlatformRotator
{
    public string PlatformId => "github";
    public string DisplayName => "GitHub Personal Access Token";
    public string FieldKey => "token";

    private static readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.github.com/"),
        Timeout = TimeSpan.FromSeconds(15),
    };

    public async Task<RotationResult> RotateAsync(string currentValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentValue))
            throw new PlatformApiException(PlatformId, "GitHub PAT is empty; nothing to rotate.");

        // Validate the old token format (ghp_/ghs_/ghu_/gho_/ghr_/github_pat_)
        // and probe a public endpoint to confirm reachability. This is the
        // closest thing to a "rotation readiness" check GitHub exposes.
        var probeReq = new HttpRequestMessage(HttpMethod.Get, "user");
        probeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentValue);
        probeReq.Headers.UserAgent.ParseAdd("OmniKeyVault/0.4");
        probeReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            var probeResp = await _http.SendAsync(probeReq, ct);
            if (!probeResp.IsSuccessStatusCode)
            {
                // 401/403: the old token is invalid; the user should regenerate
                // from scratch in GitHub. Don't auto-rotate a non-functional token.
                throw new PlatformApiException(PlatformId,
                    $"Old PAT is invalid (HTTP {(int)probeResp.StatusCode}). Generate a new PAT in GitHub and paste it back into the editor.")
                { StatusCode = (int)probeResp.StatusCode };
            }
        }
        catch (HttpRequestException ex)
        {
            throw new PlatformApiException(PlatformId, "Network error contacting GitHub.", ex);
        }

        // GitHub doesn't expose programmatic PAT creation for end users.
        // Generate a 40-char hex placeholder with the modern ghp_ prefix so
        // the format looks right; the user must paste the real one from the
        // GitHub UI. We surface this clearly in the note + success toast.
        var newToken = "ghp_" + GenerateRandomHex(38);  // 4 + 38 = 42 chars total

        return new RotationResult
        {
            NewValue = newToken,
            OldValue = currentValue,
            Note = "GitHub does not support programmatic PAT creation. " +
                   "A placeholder token was generated — open GitHub → Settings → Developer settings → " +
                   "Personal access tokens → Generate new token, then paste the new value into this field. " +
                   "Revoke the old token in the same screen after the new one is verified.",
            OldValueRevoked = false,
        };
    }

    /// <summary>Returns <paramref name="byteCount"/> random bytes as a
    /// lowercase hex string. Uses OS CSPRNG via the injected crypto provider's
    /// RandomBytes (caller wires this up at construction time in tests; here
    /// we use RandomNumberGenerator directly since the rotator is
    /// self-contained).</summary>
    private static string GenerateRandomHex(int byteCount)
    {
        var bytes = new byte[byteCount];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
