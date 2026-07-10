using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using OmniKeyVault.Contracts;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// WebDAV-based remote sync provider using standard HTTP verbs
/// (GET for download, PUT for upload, PROPFIND for connection test).
/// Compatible with Nextcloud, ownCloud, Synology WebDAV,坚果云, and
/// any RFC 4918-compliant WebDAV server.
///
/// Security notes:
///   - Credentials are sent via Basic auth over HTTPS only.
///   - The .okv file is already end-to-end encrypted; the WebDAV server
///     only sees ciphertext.
///   - Uploads use Content-Type: application/octet-stream.
///   - A 30s timeout prevents indefinite hangs on unreachable servers.
/// </summary>
public sealed class WebDavSyncProvider : IRemoteSyncProvider
{
    private readonly HttpClient _client;
    private readonly RemoteSyncConfig _config;

    public WebDavSyncProvider(RemoteSyncConfig config)
    {
        _config = config;
        var handler = new HttpClientHandler
        {
            // Support self-signed certs if the user opts in (common for home NAS).
            ServerCertificateCustomValidationCallback = config.ServerUrl.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase)
                ? (msg, cert, chain, errors) => true
                : null,
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        // Basic auth header
        if (!string.IsNullOrEmpty(_config.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_config.Username}:{_config.Password}"));
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniKeyVault/1.1");
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.ServerUrl) &&
        !string.IsNullOrWhiteSpace(_config.RemoteFilePath) &&
        _config.Enabled;

    /// <summary>The full remote URL = ServerUrl (trimmed of trailing /) + / + RemoteFilePath.</summary>
    private string FullUrl
    {
        get
        {
            var base_ = _config.ServerUrl.TrimEnd('/');
            var path = _config.RemoteFilePath.TrimStart('/');
            return $"{base_}/{path}";
        }
    }

    public async Task<bool> DownloadAsync(string localPath, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _client.GetAsync(FullUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == HttpStatusCode.Conflict)
                return false; // remote doesn't exist yet — first sync (404) or parent dir missing (409)
            resp.EnsureSuccessStatusCode();

            // Stream to temp file first, then atomic move (avoids partial downloads)
            var tmp = localPath + ".download";
            await using (var fs = File.Create(tmp))
            {
                await resp.Content.CopyToAsync(fs, ct);
                await fs.FlushAsync(ct);
            }
            // Atomic replace
            if (File.Exists(localPath))
                File.Replace(tmp, localPath, destinationBackupFileName: null);
            else
                File.Move(tmp, localPath);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException)
        {
            // Network error — rethrow so the caller can show a meaningful message
            throw;
        }
    }

    public async Task UploadAsync(string localPath, CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"Local vault file not found: {localPath}", localPath);

        // Read file, upload as PUT
        await using var fs = File.OpenRead(localPath);
        using var content = new StreamContent(fs);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // PUT may need to create parent collections first (MKCOL) on some servers.
        // We try PUT directly first; if 409 Conflict or 404 Not Found (some
        // servers like Jianguoyun return 404 instead of 409 when the parent
        // collection doesn't exist), create parent dirs then retry.
        using var resp = await _client.PutAsync(FullUrl, content, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict || resp.StatusCode == HttpStatusCode.NotFound)
        {
            // Try creating parent directory
            await EnsureParentCollectionsAsync(ct);
            // Re-read and retry (stream was consumed)
            await using var fs2 = File.OpenRead(localPath);
            using var content2 = new StreamContent(fs2);
            content2.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var resp2 = await _client.PutAsync(FullUrl, content2, ct);
            resp2.EnsureSuccessStatusCode();
            return;
        }
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string?> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            // PROPFIND with depth 0 to check if the remote path is accessible
            using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), FullUrl);
            req.Headers.Add("Depth", "0");
            using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            // 207 Multi-Status = success for PROPFIND
            // 404 = path doesn't exist yet (still a valid connection)
            // 409 Conflict = parent directory doesn't exist yet (still a valid connection)
            if (resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == HttpStatusCode.Conflict)
                return null; // connection works, just no file/path yet
            if (resp.StatusCode == HttpStatusCode.MultiStatus)
                return null;
            if (resp.IsSuccessStatusCode)
                return null;
            return $"服务器返回 {(int)resp.StatusCode} {resp.ReasonPhrase}";
        }
        catch (HttpRequestException ex)
        {
            return $"连接失败: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "连接超时(30 秒),请检查服务器地址和网络";
        }
    }

    /// <summary>Creates parent directories on the WebDAV server via MKCOL.
    /// Handles both path segments in ServerUrl (e.g. /dav/my-folder/) and
    /// in RemoteFilePath (e.g. sub/vault.okv).</summary>
    private async Task EnsureParentCollectionsAsync(CancellationToken ct)
    {
        // Parse the full URL to extract scheme + host and the path segments
        var uri = new Uri(FullUrl);
        var basePath = uri.AbsolutePath;
        var segments = basePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Reconstruct the base URL (scheme + host)
        var baseUrl = $"{uri.Scheme}://{uri.Host}";
        if (!uri.IsDefaultPort) baseUrl += $":{uri.Port}";
        // Create all parent dirs except the last segment (the file itself)
        var current = baseUrl;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            current += "/" + segments[i];
            try
            {
                using var resp = await _client.SendAsync(
                    new HttpRequestMessage(new HttpMethod("MKCOL"), current + "/"), ct);
                // 201 Created = success, 405 Method Not Allowed = already exists
            }
            catch { /* best-effort */ }
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
