using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

#nullable enable

namespace OmniKeyVault.Application;

/// <summary>
/// v1.9.1: Checks GitHub releases for application updates.
/// v2.2.0: Added direct download + auto-install — no longer requires the user
/// to open a browser and visit GitHub. The app downloads the installer .exe
/// in the background (with progress reporting) and launches it with /VERYSILENT
/// so the update is applied automatically.
///
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

    /// <summary>HttpClient for API calls (short timeout).</summary>
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>HttpClient for file downloads (long timeout, allows redirects).
    /// GitHub asset download URLs redirect from github.com to
    /// objects.githubusercontent.com, so we need HttpClientHandler with
    /// AllowAutoRedirect = true (the default).</summary>
    private static readonly HttpClient _downloadHttp = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
    })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    /// <summary>Current application version (from assembly).</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Checks GitHub for the latest release.
    /// v2.3.2: Returns a structured result that distinguishes between
    /// "no update available", "update available", and "check failed".
    /// Previously, all errors were silently swallowed and returned null,
    /// which the UI interpreted as "already on latest version" — misleading
    /// users when GitHub API rate limits or network issues occurred.</summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            req.Headers.UserAgent.ParseAdd("OmniKeyVault");
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                // GitHub API rate limit (403) or server error (5xx)
                var reason = resp.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "GitHub API 速率限制（每小时 60 次请求），请稍后再试"
                    : $"GitHub API 返回 {(int)resp.StatusCode} {resp.StatusCode}";
                return new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, reason);
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tn) ? tn.GetString() : null;
            if (string.IsNullOrEmpty(tagName))
                return new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, "GitHub API 返回的 release 数据缺少 tag_name 字段");

            // Parse version from tag (e.g. "v1.9.0" → 1.9.0)
            var versionStr = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(versionStr, out var latestVersion))
                return new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, $"无法解析版本号: {tagName}");

            if (latestVersion <= CurrentVersion)
                return new UpdateCheckResult(UpdateCheckStatus.NoUpdate, null, null);

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
                        assets.Add(new UpdateAsset(name!, url!, size));
                }
            }

            var info = new UpdateInfo(
                TagName: tagName!,
                Version: latestVersion,
                Name: releaseName ?? tagName!,
                ReleaseUrl: releaseUrl ?? "",
                Body: body ?? "",
                PublishedAt: publishedAt,
                Assets: assets
            );
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info, null);
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, "请求超时，请检查网络连接后重试");
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, $"网络请求失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, $"检查更新时发生错误: {ex.Message}");
        }
    }

    // ---- v2.2.0: Direct download + auto-install ----

    /// <summary>Finds the best installer asset from a release.
    /// Prefers the Inno Setup .exe (OmniKeyVault-Setup-x.x.x.exe) over
    /// the portable .zip, because the .exe supports silent auto-update.</summary>
    public static UpdateAsset? FindInstallerAsset(UpdateInfo info)
    {
        if (info.Assets.Count == 0) return null;

        // 1st priority: the Inno Setup installer .exe (contains "Setup" in name)
        var setup = info.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));
        if (setup != null) return setup;

        // 2nd priority: any .exe asset
        var exe = info.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (exe != null) return exe;

        // 3rd priority: the portable .zip (user can manually extract)
        var zip = info.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        return zip;
    }

    /// <summary>Downloads an update asset to a temp file with progress reporting.
    /// Returns the path to the downloaded file.
    /// Throws on network error, cancellation, or disk full.</summary>
    public async Task<string> DownloadAssetAsync(
        UpdateAsset asset,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OmniKeyVault-Update");
        Directory.CreateDirectory(tempDir);

        // Clean up old downloads in the temp directory
        try
        {
            foreach (var f in Directory.GetFiles(tempDir, "OmniKeyVault-Setup-*.exe"))
            {
                try { File.Delete(f); } catch { /* best-effort */ }
            }
            foreach (var f in Directory.GetFiles(tempDir, "OmniKeyVault-*-portable-*.zip"))
            {
                try { File.Delete(f); } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort cleanup */ }

        var destPath = Path.Combine(tempDir, asset.Name);

        var req = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        req.Headers.UserAgent.ParseAdd("OmniKeyVault");

        using var resp = await _downloadHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength ?? asset.Size;
        long receivedBytes = 0;

        await using var contentStream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            receivedBytes += bytesRead;
            progress?.Report(new DownloadProgress(receivedBytes, totalBytes));
        }

        return destPath;
    }

    /// <summary>Launches the downloaded installer and exits the current application.
    /// For .exe installers: uses /VERYSILENT /NORESTART for silent installation.
    /// For .zip archives: opens the containing folder so the user can extract manually.</summary>
    public static void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer file not found", installerPath);

        var ext = Path.GetExtension(installerPath).ToLowerInvariant();
        if (ext == ".exe")
        {
            // Inno Setup supports /VERYSILENT (no UI) and /NORESTART (we handle
            // restart ourselves). /CLOSEAPPLICATIONS ensures the running app
            // is closed before files are replaced (requires CloseApplications=yes
            // in the .iss script). /SP- disables the "This will install..." prompt.
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS /SP-",
                UseShellExecute = true,   // required for UAC elevation
                Verb = "runas",           // trigger UAC prompt for admin rights
            };
            System.Diagnostics.Process.Start(psi);
        }
        else
        {
            // For .zip: open the folder in Explorer so the user can extract manually
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Path.GetDirectoryName(installerPath)}\"",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
    }
}

/// <summary>v2.3.2: Result of an update check, distinguishing between
/// "no update", "update available", and "check failed".</summary>
public enum UpdateCheckStatus
{
    /// <summary>Current version is the latest (or newer than GitHub latest).</summary>
    NoUpdate,
    /// <summary>A newer version is available on GitHub.</summary>
    UpdateAvailable,
    /// <summary>The check failed (network error, rate limit, parse error, etc.).</summary>
    CheckFailed,
}

/// <summary>v2.3.2: Structured result returned by CheckForUpdateAsync.
/// Replaces the old nullable UpdateInfo? return type, which conflated
/// "no update" with "check failed".</summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateInfo? Info,
    string? ErrorMessage
)
{
    /// <summary>True if a newer version is available.</summary>
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable;
    /// <summary>True if the check failed (not the same as "no update").</summary>
    public bool Failed => Status == UpdateCheckStatus.CheckFailed;
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

/// <summary>Progress information for a download operation.</summary>
public sealed record DownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>Download progress as a percentage (0–100). Returns 0 if total is unknown.</summary>
    public double Percentage => TotalBytes > 0 ? (double)BytesReceived / TotalBytes * 100.0 : 0;

    /// <summary>Human-readable download speed context (received / total in MB).</summary>
    public string ReceivedMb => $"{BytesReceived / 1024.0 / 1024.0:F1}";
    public string TotalMb => $"{TotalBytes / 1024.0 / 1024.0:F1}";
}
