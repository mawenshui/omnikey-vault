using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace OmniKeyVault.Application;

/// <summary>
/// v1.9.1: Checks GitHub releases for application updates.
/// Queries the GitHub API for the latest release tag and compares
/// it with the running assembly version.
///
/// Usage:
/// - Manual check: called from SettingsWindow → shows result dialog
/// - Auto check on startup: called from GuiShell → only shows dialog if update available
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/mawenshui/omnikey-vault/releases/latest";
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>Current application version (from assembly).</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Checks GitHub for the latest release.
    /// Returns null if no update is available or if the check fails.</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            req.Headers.UserAgent.ParseAdd("OmniKeyVault");
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tn) ? tn.GetString() : null;
            if (string.IsNullOrEmpty(tagName)) return null;

            // Parse version from tag (e.g. "v1.9.0" → 1.9.0)
            var versionStr = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(versionStr, out var latestVersion)) return null;

            if (latestVersion <= CurrentVersion) return null;

            var releaseName = root.TryGetProperty("name", out var n) ? n.GetString() : tagName;
            var releaseUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : "";
            var body = root.TryGetProperty("body", out var b) ? b.GetString() : "";
            var publishedAt = root.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var dt) ? dt : (DateTime?)null;

            // Extract download URLs for assets
            var assets = new List<UpdateAsset>();
            if (root.TryGetProperty("assets", out var assetsEl))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var an) ? an.GetString() : "";
                    var url = asset.TryGetProperty("browser_download_url", out var au) ? au.GetString() : "";
                    var size = asset.TryGetProperty("size", out var asz) ? asz.GetInt64() : 0;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                        assets.Add(new UpdateAsset(name, url, size));
                }
            }

            return new UpdateInfo(
                TagName: tagName,
                Version: latestVersion,
                Name: releaseName ?? tagName,
                ReleaseUrl: releaseUrl ?? "",
                Body: body ?? "",
                PublishedAt: publishedAt,
                Assets: assets
            );
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Information about an available update.</summary>
public sealed record UpdateInfo(
    string TagName,
    Version Version,
    string Name,
    string ReleaseUrl,
    string Body,
    DateTime? PublishedAt,
    IReadOnlyList<UpdateAsset> Assets
);

/// <summary>A single downloadable asset from a GitHub release.</summary>
public sealed record UpdateAsset(string Name, string DownloadUrl, long Size);
