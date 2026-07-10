using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Contracts;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.WebDav;

/// <summary>
/// Integration tests for WebDavSyncProvider against a real WebDAV server (Jianguoyun/坚果云).
/// These tests require network access and valid credentials configured via environment variables:
///   OKV_WEBDAV_URL      – e.g. https://dav.jianguoyun.com/dav/omnikey-vault-test/
///   OKV_WEBDAV_USER     – e.g. user@example.com
///   OKV_WEBDAV_PASSWORD – app-specific password
/// If any variable is missing, all tests are skipped (not failed).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "WebDav")]
public class WebDavIntegrationTests : IDisposable
{
    private readonly string _serverUrl;
    private readonly string _username;
    private readonly string _password;
    private readonly string _remoteFilePath;
    private readonly RemoteSyncConfig _config;
    private readonly WebDavSyncProvider _provider;
    private readonly string _tempDir;
    private readonly bool _enabled;

    public WebDavIntegrationTests()
    {
        _serverUrl = Environment.GetEnvironmentVariable("OKV_WEBDAV_URL") ?? "";
        _username = Environment.GetEnvironmentVariable("OKV_WEBDAV_USER") ?? "";
        _password = Environment.GetEnvironmentVariable("OKV_WEBDAV_PASSWORD") ?? "";
        _enabled = !string.IsNullOrWhiteSpace(_serverUrl)
                   && !string.IsNullOrWhiteSpace(_username)
                   && !string.IsNullOrWhiteSpace(_password);

        // Unique file name per test run to avoid collisions
        _remoteFilePath = $"test-{Guid.NewGuid():N}.okv";

        _config = new RemoteSyncConfig
        {
            ServerUrl = _serverUrl,
            Username = _username,
            Password = _password,
            RemoteFilePath = _remoteFilePath,
            Enabled = true,
        };

        _provider = new WebDavSyncProvider(_config);
        _tempDir = Path.Combine(Path.GetTempPath(), "okv-webdav-xunit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    private bool SkipIfNotConfigured()
    {
        if (!_enabled)
        {
            // Not configured — skip silently (test passes without running).
            // Set OKV_WEBDAV_URL / OKV_WEBDAV_USER / OKV_WEBDAV_PASSWORD env vars to enable.
            return true;
        }
        return false;
    }

    [Fact]
    public async Task Connection_Probe_ReturnsNull_WhenServerIsReachable()
    {
        if (SkipIfNotConfigured()) return;
        var error = await _provider.TestConnectionAsync();
        error.Should().BeNull("server should be reachable with valid credentials");
    }

    [Fact]
    public async Task Upload_Then_Download_RoundTrip_PreservesData()
    {
        if (SkipIfNotConfigured()) return;
        var uploadPath = Path.Combine(_tempDir, "upload.okv");
        var data = new byte[] { 0x4F, 0x4B, 0x56, 0x31, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        await File.WriteAllBytesAsync(uploadPath, data);

        await _provider.UploadAsync(uploadPath);

        var downloadPath = Path.Combine(_tempDir, "download.okv");
        var exists = await _provider.DownloadAsync(downloadPath);
        exists.Should().BeTrue("uploaded file should be downloadable");

        var downloaded = await File.ReadAllBytesAsync(downloadPath);
        downloaded.Should().Equal(data, "downloaded content must match uploaded content");
    }

    [Fact]
    public async Task Download_NonExistent_Remote_ReturnsFalse()
    {
        if (SkipIfNotConfigured()) return;
        var dummyPath = Path.Combine(_tempDir, "dummy.okv");
        var exists = await _provider.DownloadAsync(dummyPath);
        exists.Should().BeFalse("non-existent remote file should return false, not throw");
    }

    [Fact]
    public async Task Upload_LargeFile_RoundTrip_PreservesData()
    {
        if (SkipIfNotConfigured()) return;
        var rng = new Random(42);
        var data = new byte[4096];
        rng.NextBytes(data);

        var uploadPath = Path.Combine(_tempDir, "large.okv");
        await File.WriteAllBytesAsync(uploadPath, data);
        await _provider.UploadAsync(uploadPath);

        var downloadPath = Path.Combine(_tempDir, "large-back.okv");
        var exists = await _provider.DownloadAsync(downloadPath);
        exists.Should().BeTrue();

        var downloaded = await File.ReadAllBytesAsync(downloadPath);
        downloaded.Should().Equal(data);
    }

    [Fact]
    public async Task Upload_Overwrite_Replaces_RemoteContent()
    {
        if (SkipIfNotConfigured()) return;
        var path = Path.Combine(_tempDir, "overwrite.okv");
        await File.WriteAllBytesAsync(path, new byte[] { 0x01, 0x02, 0x03 });
        await _provider.UploadAsync(path);

        // Overwrite with new data
        var newData = new byte[] { 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA };
        await File.WriteAllBytesAsync(path, newData);
        await _provider.UploadAsync(path);

        var downloadPath = Path.Combine(_tempDir, "overwrite-back.okv");
        var exists = await _provider.DownloadAsync(downloadPath);
        exists.Should().BeTrue();

        var downloaded = await File.ReadAllBytesAsync(downloadPath);
        downloaded.Should().Equal(newData, "overwrite should replace old content");
    }

    [Fact]
    public async Task RealVault_Upload_Download_Unlock_RoundTrip()
    {
        if (SkipIfNotConfigured()) return;
        var crypto = new SodiumCryptoProvider();
        var format = new VaultFormat();
        var lockSvc = new LockService(crypto);
        var codec = new ProfilePayloadCodec();
        var deviceId = "webdav-xunit-device";
        var vault = new VaultService(crypto, format, lockSvc, codec, deviceId, new DeviceKeystore());

        var vaultPath = Path.Combine(_tempDir, "vault.okv");
        var pw = Encoding.UTF8.GetBytes("WebDavXunitP@ss2026");
        await vault.CreateAsync(vaultPath, "webdav-xunit-vault", pw, Argon2Params.ForTests(64 * 1024 * 1024));
        vault.Lock();

        await _provider.UploadAsync(vaultPath);

        var downloadedVaultPath = Path.Combine(_tempDir, "vault-downloaded.okv");
        var exists = await _provider.DownloadAsync(downloadedVaultPath);
        exists.Should().BeTrue();

        // Size should match
        new FileInfo(vaultPath).Length.Should().Be(new FileInfo(downloadedVaultPath).Length);

        // Should be able to unlock the downloaded vault with the same password
        var vault2 = new VaultService(crypto, format, new LockService(crypto), codec, deviceId, new DeviceKeystore());
        await vault2.UnlockAsync(downloadedVaultPath, pw);
        vault2.IsUnlocked.Should().BeTrue("downloaded vault must be unlockable with the same password");
        vault2.Lock();
        vault2.Dispose();
        vault.Dispose();
    }

    [Fact]
    public async Task WebDavSyncService_FirstSync_UploadsVault()
    {
        if (SkipIfNotConfigured()) return;
        var crypto = new SodiumCryptoProvider();
        var format = new VaultFormat();
        var lockSvc = new LockService(crypto);
        var codec = new ProfilePayloadCodec();
        var manifests = new ManifestService();
        var deviceId = "webdav-sync-xunit";
        var vault = new VaultService(crypto, format, lockSvc, codec, deviceId, new DeviceKeystore());
        var syncSvc = new SyncService(vault, lockSvc, crypto, format, codec, manifests, deviceId);

        var vaultPath = Path.Combine(_tempDir, "sync-vault.okv");
        var pw = Encoding.UTF8.GetBytes("SyncXunitP@ss2026!");
        await vault.CreateAsync(vaultPath, "sync-xunit", pw, Argon2Params.ForTests(64 * 1024 * 1024));

        // Add an entry
        vault.PutEntry("prod", new Entry
        {
            Id = Guid.NewGuid(),
            Name = "WebDAV XUnit Entry",
            PlatformId = "github",
            Type = EntryType.ApiKey,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            Fields = new List<Field>
            {
                new() { Key = "token", Value = FieldCodec.Encode("ghp_testtoken_webdav_xunit"), Kind = FieldKind.Secret, Sensitive = true },
            },
            Tags = new List<string> { "test", "webdav" },
            Notes = "Created for WebDAV sync xunit test",
        });
        await vault.SaveAsync();

        IRemoteSyncProvider? ProviderFactory() => new WebDavSyncProvider(_config);
        var webDavSync = new WebDavSyncService(syncSvc, ProviderFactory);

        var result = await webDavSync.SyncAsync(vaultPath);
        result.Uploaded.Should().BeTrue("first sync should upload the vault to remote");
        result.Message.Should().NotBeNullOrEmpty("sync should have a status message");

        vault.Lock();
        vault.Dispose();
    }

    /// <summary>
    /// Simulates the cross-device scenario: Device A uploads a vault, then
    /// Device B (with a different vault/UUID) tries to sync. The sync should
    /// detect the UUID mismatch and handle it gracefully:
    ///   - If Device B's vault is empty → TakeRemote (pull the remote vault)
    ///   - If Device B's vault has entries → RemoteVaultMismatch error
    /// </summary>
    [Fact]
    public async Task CrossDeviceSync_EmptyLocal_TakesRemoteVault()
    {
        if (SkipIfNotConfigured()) return;

        // --- Device A: create a vault with entries, sync to WebDAV ---
        var cryptoA = new SodiumCryptoProvider();
        var formatA = new VaultFormat();
        var lockA = new LockService(cryptoA);
        var codecA = new ProfilePayloadCodec();
        var manifestsA = new ManifestService();
        var deviceA = "device-a-cross";
        var vaultA = new VaultService(cryptoA, formatA, lockA, codecA, deviceA, new DeviceKeystore());
        var syncSvcA = new SyncService(vaultA, lockA, cryptoA, formatA, codecA, manifestsA, deviceA);

        var vaultPathA = Path.Combine(_tempDir, "device-a-vault.okv");
        var pwA = Encoding.UTF8.GetBytes("CrossDeviceP@ss2026");
        await vaultA.CreateAsync(vaultPathA, "cross-device-vault", pwA, Argon2Params.ForTests(64 * 1024 * 1024));

        // Add an entry on Device A
        vaultA.PutEntry("prod", new Entry
        {
            Id = Guid.NewGuid(),
            Name = "Device A Entry",
            PlatformId = "github",
            Type = EntryType.ApiKey,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            Fields = new List<Field>
            {
                new() { Key = "token", Value = FieldCodec.Encode("ghp_cross_device_token"), Kind = FieldKind.Secret, Sensitive = true },
            },
            Tags = new List<string> { "cross-device" },
            Notes = "Created on Device A",
        });
        await vaultA.SaveAsync();

        IRemoteSyncProvider? ProviderFactoryA() => new WebDavSyncProvider(_config);
        var webDavA = new WebDavSyncService(syncSvcA, ProviderFactoryA);
        var resultA = await webDavA.SyncAsync(vaultPathA);
        resultA.Uploaded.Should().BeTrue("Device A should upload its vault on first sync");

        vaultA.Lock();
        vaultA.Dispose();

        // --- Device B: create a NEW empty vault (different UUID, different KEK) ---
        var cryptoB = new SodiumCryptoProvider();
        var formatB = new VaultFormat();
        var lockB = new LockService(cryptoB);
        var codecB = new ProfilePayloadCodec();
        var manifestsB = new ManifestService();
        var deviceB = "device-b-cross";
        var vaultB = new VaultService(cryptoB, formatB, lockB, codecB, deviceB, new DeviceKeystore());
        var syncSvcB = new SyncService(vaultB, lockB, cryptoB, formatB, codecB, manifestsB, deviceB);

        var vaultPathB = Path.Combine(_tempDir, "device-b-vault.okv");
        var pwB = Encoding.UTF8.GetBytes("DifferentPassword123!");  // Different password!
        await vaultB.CreateAsync(vaultPathB, "device-b-vault", pwB, Argon2Params.ForTests(64 * 1024 * 1024));
        // Note: Device B's vault is empty (no entries added)

        IRemoteSyncProvider? ProviderFactoryB() => new WebDavSyncProvider(_config);
        var webDavB = new WebDavSyncService(syncSvcB, ProviderFactoryB);

        // --- Act: Device B syncs with WebDAV (remote has Device A's vault) ---
        var resultB = await webDavB.SyncAsync(vaultPathB);

        // --- Assert: should TakeRemote (empty local vault replaced by remote) ---
        resultB.SyncResult.Should().NotBeNull();
        resultB.SyncResult!.Outcome.Should().Be(SyncOutcome.TookRemote,
            "empty local vault should be replaced by the remote vault");
        resultB.Message.Should().Contain("重新解锁", "user should be told to re-unlock with the remote vault's password");

        // The local vault file should now be the remote vault (different UUID than Device B's original)
        var recordB = await formatB.ReadAsync(vaultPathB);
        var recordA2 = await formatA.ReadAsync(vaultPathA);
        recordB.VaultUuid.Should().Be(recordA2.VaultUuid,
            "Device B's local file should now contain Device A's vault UUID");

        vaultB.Lock();
        vaultB.Dispose();
    }

    /// <summary>
    /// When Device B has a vault with entries and tries to sync with a different
    /// remote vault, the sync should return RemoteVaultMismatch (not crash).
    /// </summary>
    [Fact]
    public async Task CrossDeviceSync_NonEmptyLocal_ReturnsMismatchError()
    {
        if (SkipIfNotConfigured()) return;

        // --- Device A: create and upload a vault with entries ---
        var cryptoA = new SodiumCryptoProvider();
        var formatA = new VaultFormat();
        var lockA = new LockService(cryptoA);
        var codecA = new ProfilePayloadCodec();
        var manifestsA = new ManifestService();
        var deviceA = "device-a-mismatch";
        var vaultA = new VaultService(cryptoA, formatA, lockA, codecA, deviceA, new DeviceKeystore());
        var syncSvcA = new SyncService(vaultA, lockA, cryptoA, formatA, codecA, manifestsA, deviceA);

        var vaultPathA = Path.Combine(_tempDir, "mismatch-a.okv");
        var pwA = Encoding.UTF8.GetBytes("MismatchTestP@ss1");
        await vaultA.CreateAsync(vaultPathA, "mismatch-a", pwA, Argon2Params.ForTests(64 * 1024 * 1024));
        vaultA.PutEntry("prod", new Entry
        {
            Id = Guid.NewGuid(),
            Name = "A Entry",
            PlatformId = "test",
            Type = EntryType.ApiKey,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            Fields = new List<Field> { new() { Key = "k", Value = FieldCodec.Encode("v"), Kind = FieldKind.Secret, Sensitive = true } },
        });
        await vaultA.SaveAsync();

        IRemoteSyncProvider? ProviderFactoryA() => new WebDavSyncProvider(_config);
        var webDavA = new WebDavSyncService(syncSvcA, ProviderFactoryA);
        await webDavA.SyncAsync(vaultPathA);
        vaultA.Lock();
        vaultA.Dispose();

        // --- Device B: create a vault WITH entries, then try to sync ---
        var cryptoB = new SodiumCryptoProvider();
        var formatB = new VaultFormat();
        var lockB = new LockService(cryptoB);
        var codecB = new ProfilePayloadCodec();
        var manifestsB = new ManifestService();
        var deviceB = "device-b-mismatch";
        var vaultB = new VaultService(cryptoB, formatB, lockB, codecB, deviceB, new DeviceKeystore());
        var syncSvcB = new SyncService(vaultB, lockB, cryptoB, formatB, codecB, manifestsB, deviceB);

        var vaultPathB = Path.Combine(_tempDir, "mismatch-b.okv");
        var pwB = Encoding.UTF8.GetBytes("DifferentP@ss2");
        await vaultB.CreateAsync(vaultPathB, "mismatch-b", pwB, Argon2Params.ForTests(64 * 1024 * 1024));
        // Add an entry so the vault is NOT empty
        vaultB.PutEntry("prod", new Entry
        {
            Id = Guid.NewGuid(),
            Name = "B Entry",
            PlatformId = "test",
            Type = EntryType.ApiKey,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            Fields = new List<Field> { new() { Key = "k", Value = FieldCodec.Encode("v"), Kind = FieldKind.Secret, Sensitive = true } },
        });
        await vaultB.SaveAsync();

        IRemoteSyncProvider? ProviderFactoryB() => new WebDavSyncProvider(_config);
        var webDavB = new WebDavSyncService(syncSvcB, ProviderFactoryB);

        // --- Act: Device B syncs — should get RemoteVaultMismatch, NOT crash ---
        var resultB = await webDavB.SyncAsync(vaultPathB);

        // --- Assert ---
        resultB.SyncResult.Should().NotBeNull();
        resultB.SyncResult!.Outcome.Should().Be(SyncOutcome.RemoteVaultMismatch);
        resultB.Uploaded.Should().BeFalse("should not upload when there's a mismatch");
        resultB.Message.Should().Contain("不匹配", "error message should explain the mismatch");

        vaultB.Lock();
        vaultB.Dispose();
    }

    public void Dispose()
    {
        // Clean up remote test file
        if (_enabled)
        {
            try
            {
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
                var fullUrl = _serverUrl.TrimEnd('/') + "/" + _remoteFilePath.TrimStart('/');
                client.DeleteAsync(fullUrl).GetAwaiter().GetResult();
                client.Dispose();
            }
            catch { /* best-effort cleanup */ }
        }

        _provider?.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
