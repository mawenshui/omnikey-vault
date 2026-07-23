using System.Net;
using System.Text.Json;

namespace OmniKeyVault.Application;

/// <summary>
/// v1.9: Read-only local HTTP API server for browser extensions.
/// Listens on 127.0.0.1:14725 (configurable) and exposes a minimal
/// JSON API that allows the browser extension to:
/// - Check vault status (locked/unlocked)
/// - Search entries by name (returns names + masked fields only)
/// - Copy a field value to clipboard (no raw values over HTTP)
///
/// Security model:
/// - Loopback only (127.0.0.1) — never reachable from the network
/// - CORS restricted to chrome-extension:// and moz-extension:// origins
/// - All responses are read-only — no mutation operations
/// - Raw secret values are NEVER sent over HTTP; the extension
///   requests a "copy to clipboard" action instead
/// - A per-session bearer token is required for authentication
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class BrowserExtensionApiService : IDisposable
{
    private readonly VaultService _vault;
    private readonly EntryService _entries;
    private readonly ClipboardService _clipboard;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _authToken = string.Empty;
    private bool _running;

    /// <summary>The default port for the local API server.</summary>
    public const int DefaultPort = 14725;
    public int Port { get; private set; } = DefaultPort;
    public bool IsRunning => _running;
    public string AuthToken => _authToken;

    public BrowserExtensionApiService(VaultService vault, EntryService entries, ClipboardService clipboard)
    {
        _vault = vault;
        _entries = entries;
        _clipboard = clipboard;
        _authToken = GenerateToken();
    }

    /// <summary>Starts the HTTP listener on 127.0.0.1:port.</summary>
    public void Start(int port = DefaultPort)
    {
        if (_running) return;
        Port = port;
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _running = true;
        _ = ListenLoop(_cts.Token);
    }

    /// <summary>Stops the HTTP listener.</summary>
    public void Stop()
    {
        if (!_running) return;
        _cts?.Cancel();
        _listener?.Stop();
        _running = false;
    }

    /// <summary>Regenerates the auth token. The browser extension must be re-paired.</summary>
    public void RegenerateToken()
    {
        _authToken = GenerateToken();
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = HandleRequest(ctx);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* best-effort: keep listening */ }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            // CORS headers for browser extension
            var origin = ctx.Request.Headers["Origin"] ?? "";
            if (origin.StartsWith("chrome-extension://") || origin.StartsWith("moz-extension://"))
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
            }
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";

            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            // Auth check
            var auth = ctx.Request.Headers["Authorization"] ?? "";
            if (!auth.Equals($"Bearer {_authToken}", StringComparison.Ordinal))
            {
                await WriteJson(ctx, 401, new { error = "unauthorized" });
                return;
            }

            var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimStart('/');

            switch (path)
            {
                case "api/status":
                    await WriteJson(ctx, 200, new
                    {
                        locked = !_vault.IsUnlocked,
                        vaultPath = _vault.CurrentVaultPath,
                        profiles = _vault.IsUnlocked ? _vault.ListProfileNames() : Array.Empty<string>(),
                    });
                    break;

                case "api/search":
                    var query = ctx.Request.QueryString["q"] ?? "";
                    var profile = ctx.Request.QueryString["profile"] ?? "prod";
                    if (!_vault.IsUnlocked)
                    {
                        await WriteJson(ctx, 403, new { error = "vault_locked" });
                        break;
                    }
                    var results = SearchEntries(profile, query);
                    await WriteJson(ctx, 200, results);
                    break;

                case "api/copy":
                    if (!_vault.IsUnlocked)
                    {
                        await WriteJson(ctx, 403, new { error = "vault_locked" });
                        break;
                    }
                    var entryIdStr = ctx.Request.QueryString["entryId"] ?? "";
                    var fieldKey = ctx.Request.QueryString["field"] ?? "";
                    if (!Guid.TryParse(entryIdStr, out var entryId))
                    {
                        await WriteJson(ctx, 400, new { error = "invalid_entry_id" });
                        break;
                    }
                    var copyProfile = ctx.Request.QueryString["profile"] ?? "prod";
                    var entry = _vault.GetEntry(copyProfile, entryId);
                    if (entry == null)
                    {
                        await WriteJson(ctx, 404, new { error = "entry_not_found" });
                        break;
                    }
                    var field = entry.FindField(fieldKey);
                    if (field == null)
                    {
                        await WriteJson(ctx, 404, new { error = "field_not_found" });
                        break;
                    }
                    // Copy to clipboard — never send the value over HTTP
                    _clipboard.CopySensitive(field.ValueString);
                    await Task.CompletedTask;
                    await WriteJson(ctx, 200, new { success = true, message = "copied_to_clipboard" });
                    break;

                default:
                    await WriteJson(ctx, 404, new { error = "not_found" });
                    break;
            }
        }
        catch (Exception ex)
        {
            try { await WriteJson(ctx, 500, new { error = "internal", message = ex.Message }); }
            catch { /* best-effort */ }
        }
    }

    private object SearchEntries(string profile, string query)
    {
        try
        {
            // v2.3.6: Fix parameter order — the query must go into the `search`
            // parameter (4th), not the `tag` parameter (2nd). The previous call
            // _entries.List(profile, query, null, null) passed the search text as
            // a tag filter, which only matched entries with an exactly matching
            // tag — so searches almost always returned zero results.
            var entries = _entries.List(profile, null, null, query);
            var results = entries.Select(e => new
            {
                id = e.Id,
                name = e.Name,
                platformId = e.PlatformId,
                type = e.Type.ToString(),
                fields = e.Fields.Select(f => new
                {
                    key = f.Key,
                    sensitive = f.Sensitive,
                    masked = f.Sensitive ? f.DisplayMask() : f.ValueString,
                }),
            });
            return new { results, count = entries.Count };
        }
        catch
        {
            return new { results = Array.Empty<object>(), count = 0 };
        }
    }

    private static async Task WriteJson(HttpListenerContext ctx, int statusCode, object data)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(data);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static string GenerateToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
