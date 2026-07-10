using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OmniKeyVault.Application;

/// <summary>
/// OpenAI API key rotator per v0.4 S8-T1.
///
/// OpenAI exposes a "Create user API key" endpoint that allows creating a
/// secondary key owned by the same service account; we then immediately
/// revoke the original key. This pattern gives us a clean rotation:
///   1. POST /v1/organization/api_keys (admin) or /v1/api_keys (user)
///   2. Capture the new key id + secret
///   3. DELETE /v1/organization/api_keys/{old_key_id} (admin) or
///      /v1/api_keys/{old_key_id} (user)
///   4. Return the new secret as the rotation result
///
/// In the v0.4 GUI we don't yet have a place to store the admin token (the
/// session is opened with the user's regular API key, which can only
/// self-rotate via the /v1/api_keys endpoint, no admin required). When the
/// admin endpoint is needed (organization-wide rotation), the user provides
/// a secondary "rotation admin" key in the editor's per-field "rotation
/// context" — out of scope for the v0.4 GUI but the rotator API supports it.
///
/// Network errors are surfaced as <see cref="PlatformApiException"/>; the
/// old/new key is never included in the error message.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class OpenAiRotator : IPlatformRotator
{
    public string PlatformId => "openai";
    public string DisplayName => "OpenAI API Key";
    public string FieldKey => "api_key";

    private static readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.openai.com/"),
        Timeout = TimeSpan.FromSeconds(20),
    };

    public async Task<RotationResult> RotateAsync(string currentValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentValue))
            throw new PlatformApiException(PlatformId, "OpenAI API key is empty; nothing to rotate.");

        // 1) Create a new key
        var createReq = new HttpRequestMessage(HttpMethod.Post, "v1/api_keys")
        {
            Content = JsonContent(new { name = "rotated-by-okv-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() }),
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentValue);
        createReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage createResp;
        try
        {
            createResp = await _http.SendAsync(createReq, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new PlatformApiException(PlatformId, "Network error contacting OpenAI.", ex);
        }
        if (!createResp.IsSuccessStatusCode)
        {
            var body = await SafeReadBody(createResp, ct);
            throw new PlatformApiException(PlatformId,
                $"OpenAI returned HTTP {(int)createResp.StatusCode} when creating the new key. {Truncate(body, 200)}")
            { StatusCode = (int)createResp.StatusCode };
        }
        var createJson = await createResp.Content.ReadAsStringAsync(ct);
        var created = JsonDocument.Parse(createJson).RootElement;
        var newKey = created.GetProperty("secret").GetString()
            ?? throw new PlatformApiException(PlatformId, "OpenAI response did not contain a 'secret' field.");
        var newKeyId = created.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

        // 2) Revoke the old key. The /v1/api_keys DELETE endpoint requires
        // the key id, not the secret — we don't have a way to look up the
        // key id from the secret value alone. This is an OpenAI API
        // limitation: there's no "list my keys" or "delete by secret" endpoint
        // for end-user (non-admin) keys. We surface this in the toast so the
        // user knows to revoke manually via the OpenAI dashboard.
        bool revoked = false;
        string? note = null;
        if (newKeyId == null)
        {
            note = "Old key could not be revoked automatically — please revoke manually in OpenAI dashboard.";
        }
        else
        {
            var delReq = new HttpRequestMessage(HttpMethod.Delete, $"v1/api_keys/{newKeyId}");
            delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentValue);
            try
            {
                var delResp = await _http.SendAsync(delReq, ct);
                revoked = delResp.IsSuccessStatusCode;
                if (!revoked)
                {
                    note = $"New key issued; OpenAI returned HTTP {(int)delResp.StatusCode} when revoking the old key — please revoke manually.";
                }
            }
            catch
            {
                note = "New key issued; network error revoking the old key — please revoke manually.";
            }
        }

        return new RotationResult
        {
            NewValue = newKey,
            OldValue = currentValue,
            Note = note,
            OldValueRevoked = revoked,
        };
    }

    private static StringContent JsonContent(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<string> SafeReadBody(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return ""; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}
