namespace OmniKeyVault.Contracts;

/// <summary>
/// Abstracts remote storage sync (WebDAV, S3, etc.) for vault files.
/// The provider downloads the remote .okv file to a local temp path,
/// and uploads the local vault back to the remote server after sync.
/// </summary>
public interface IRemoteSyncProvider : IDisposable
{
    /// <summary>Whether the provider is configured (has valid credentials + URL).</summary>
    bool IsConfigured { get; }

    /// <summary>Downloads the remote vault file to the specified local path.
    /// Returns false if the remote file does not exist (first sync / new device).</summary>
    Task<bool> DownloadAsync(string localPath, CancellationToken ct = default);

    /// <summary>Uploads the local vault file to the remote server.</summary>
    Task UploadAsync(string localPath, CancellationToken ct = default);

    /// <summary>Tests the connection by issuing a simple PROPFIND or HEAD request.
    /// Returns an error message on failure, or null on success.</summary>
    Task<string?> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>Configuration for remote sync providers.</summary>
public sealed class RemoteSyncConfig
{
    /// <summary>Remote server base URL (e.g. https://dav.example.com/okv/).</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>Username for basic auth.</summary>
    public string Username { get; set; } = "";

    /// <summary>Password for basic auth.</summary>
    public string Password { get; set; } = "";

    /// <summary>Remote file path relative to ServerUrl (e.g. vault.okv).</summary>
    public string RemoteFilePath { get; set; } = "vault.okv";

    /// <summary>Whether remote sync is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether to auto-sync on startup and on file changes.</summary>
    public bool AutoSync { get; set; }
}
