using System.IO;
using OmniKeyVault.Contracts;

namespace OmniKeyVault.Application;

/// <summary>
/// Orchestrates WebDAV-based remote sync:
///   1. Download the remote .okv file to a temp path.
///   2. Call SyncService.SyncAsync to merge local + remote.
///   3. Upload the merged local .okv back to the remote server.
///
/// This service wraps the existing file-based SyncService so all vector-clock
/// comparison, merge logic, and conflict resolution still apply.
/// </summary>
public sealed class WebDavSyncService
{
    private readonly SyncService _syncService;
    private readonly Func<IRemoteSyncProvider?> _providerFactory;

    public WebDavSyncService(SyncService syncService, Func<IRemoteSyncProvider?> providerFactory)
    {
        _syncService = syncService;
        _providerFactory = providerFactory;
    }

    /// <summary>Performs a full WebDAV sync cycle.
    /// Returns the SyncResult from the underlying SyncService, plus a
    /// human-readable summary of the upload outcome.</summary>
    public async Task<WebDavSyncResult> SyncAsync(string vaultFilePath, CancellationToken ct = default)
    {
        var provider = _providerFactory();
        if (provider == null || !provider.IsConfigured)
            return new WebDavSyncResult(null, false, "WebDAV 未配置,请先在设置中填写服务器信息。");

        // 1. Download remote file to temp path
        var tempRemote = Path.Combine(Path.GetTempPath(), $"okv-webdav-remote-{Guid.NewGuid():N}.okv");
        bool remoteExists;
        try
        {
            remoteExists = await provider.DownloadAsync(tempRemote, ct);
        }
        catch (Exception ex)
        {
            return new WebDavSyncResult(null, false, $"下载远端文件失败: {ex.Message}");
        }

        SyncResult? syncResult = null;
        try
        {
            if (!remoteExists)
            {
                // Remote doesn't exist — this is the first push.
                // Just upload the local vault.
                try
                {
                    await provider.UploadAsync(vaultFilePath, ct);
                    return new WebDavSyncResult(null, true, "首次同步:已将本地金库上传到 WebDAV 服务器。");
                }
                catch (Exception ex)
                {
                    return new WebDavSyncResult(null, false, $"上传失败: {ex.Message}");
                }
            }

            // 2. Sync local with the downloaded remote file
            try
            {
                syncResult = await _syncService.SyncAsync(vaultFilePath, tempRemote, ct);
            }
            catch (Exception ex)
            {
                return new WebDavSyncResult(null, false, $"本地合并失败: {ex.Message}");
            }

            // 3. Handle the sync outcome
            // RemoteVaultMismatch: different vault instances, cannot merge.
            if (syncResult.Outcome == SyncOutcome.RemoteVaultMismatch)
            {
                return new WebDavSyncResult(syncResult, false, syncResult.Message);
            }

            // NoChange or LocalAhead
            if (syncResult.Outcome == SyncOutcome.NoChange ||
                syncResult.Outcome == SyncOutcome.LocalAhead)
            {
                // For LocalAhead: we should push our newer version to the server.
                if (syncResult.Outcome == SyncOutcome.LocalAhead)
                {
                    try
                    {
                        await provider.UploadAsync(vaultFilePath, ct);
                        return new WebDavSyncResult(syncResult, true,
                            "本地版本更新,已推送到 WebDAV 服务器。");
                    }
                    catch (Exception ex)
                    {
                        return new WebDavSyncResult(syncResult, false,
                            $"本地版本更新但推送失败: {ex.Message}");
                    }
                }
                return new WebDavSyncResult(syncResult, true, "已是最新,无需同步。");
            }

            // TookRemote from UUID mismatch (empty local vault replaced by remote):
            // the local file is now identical to the remote — no need to upload.
            // The user needs to re-unlock with the remote vault's password.
            if (syncResult.Outcome == SyncOutcome.TookRemote &&
                syncResult.Message.Contains("重新解锁"))
            {
                return new WebDavSyncResult(syncResult, false, syncResult.Message);
            }

            // TookRemote (same vault, local was behind) or Merged:
            // the local file has been updated. Upload it back so other devices can sync.
            try
            {
                await provider.UploadAsync(vaultFilePath, ct);
                var msg = syncResult.Outcome == SyncOutcome.TookRemote
                    ? "已从远端拉取更新并同步完成。"
                    : $"已合并 {syncResult.EntriesMerged} 个条目并同步到远端。";
                return new WebDavSyncResult(syncResult, true, msg);
            }
            catch (Exception ex)
            {
                return new WebDavSyncResult(syncResult, false,
                    $"本地合并成功但上传远端失败: {ex.Message}");
            }
        }
        finally
        {
            // Clean up temp file
            try { if (File.Exists(tempRemote)) File.Delete(tempRemote); } catch { }
        }
    }

    /// <summary>Tests the WebDAV connection by delegating to the provider.</summary>
    public async Task<string?> TestConnectionAsync(CancellationToken ct = default)
    {
        var provider = _providerFactory();
        if (provider == null)
            return "WebDAV 提供者未初始化。";
        return await provider.TestConnectionAsync(ct);
    }

    /// <summary>Pulls the remote vault from WebDAV and merges with local.
    /// Use this when you want to fetch changes from the cloud.</summary>
    public async Task<WebDavSyncResult> PullAsync(string vaultFilePath, CancellationToken ct = default)
    {
        var provider = _providerFactory();
        if (provider == null || !provider.IsConfigured)
            return new WebDavSyncResult(null, false, "WebDAV 未配置,请先在设置中填写服务器信息。");

        var tempRemote = Path.Combine(Path.GetTempPath(), $"okv-webdav-pull-{Guid.NewGuid():N}.okv");
        bool remoteExists;
        try
        {
            remoteExists = await provider.DownloadAsync(tempRemote, ct);
        }
        catch (Exception ex)
        {
            return new WebDavSyncResult(null, false, $"下载远端文件失败: {ex.Message}");
        }

        try
        {
            if (!remoteExists)
            {
                return new WebDavSyncResult(null, false, "远端没有金库文件,无法拉取。");
            }

            // Merge remote with local
            var syncResult = await _syncService.SyncAsync(vaultFilePath, tempRemote, ct);

            // RemoteVaultMismatch: different vault instances, cannot merge.
            if (syncResult.Outcome == SyncOutcome.RemoteVaultMismatch)
            {
                return new WebDavSyncResult(syncResult, false, syncResult.Message);
            }

            // NoChange or LocalAhead: local is newer, nothing to pull
            if (syncResult.Outcome == SyncOutcome.NoChange || syncResult.Outcome == SyncOutcome.LocalAhead)
            {
                return new WebDavSyncResult(syncResult, true, "本地已是最新,无需拉取。");
            }

            // TookRemote or Merged: local has been updated with remote changes
            var msg = syncResult.Outcome == SyncOutcome.TookRemote
                ? "已从远端拉取更新。"
                : $"已合并 {syncResult.EntriesMerged} 个条目。";
            return new WebDavSyncResult(syncResult, true, msg);
        }
        catch (Exception ex)
        {
            return new WebDavSyncResult(null, false, $"拉取合并失败: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempRemote)) File.Delete(tempRemote); } catch { }
        }
    }

    /// <summary>Pushes the local vault to WebDAV without pulling first.
    /// Use this when you want to push your local changes to the cloud.</summary>
    public async Task<WebDavSyncResult> PushAsync(string vaultFilePath, CancellationToken ct = default)
    {
        var provider = _providerFactory();
        if (provider == null || !provider.IsConfigured)
            return new WebDavSyncResult(null, false, "WebDAV 未配置,请先在设置中填写服务器信息。");

        try
        {
            await provider.UploadAsync(vaultFilePath, ct);
            return new WebDavSyncResult(null, true, "已推送到 WebDAV 服务器。");
        }
        catch (Exception ex)
        {
            return new WebDavSyncResult(null, false, $"推送失败: {ex.Message}");
        }
    }
}

/// <summary>Result of a WebDAV sync operation.</summary>
public sealed record WebDavSyncResult(
    SyncResult? SyncResult,
    bool Uploaded,
    string Message
);
