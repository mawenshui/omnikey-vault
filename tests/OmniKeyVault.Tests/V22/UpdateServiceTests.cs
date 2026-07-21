using System.IO;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V22;

/// <summary>
/// v2.2.0: Tests for the direct-download + auto-install update feature.
/// Covers FindInstallerAsset priority logic, DownloadProgress calculation,
/// and DownloadAssetAsync with a local HTTP listener.
/// </summary>
public class UpdateServiceTests
{
    private readonly UpdateService _svc = new();

    // ---- FindInstallerAsset ----

    [Fact]
    public void FindInstallerAsset_PrefersSetupExe()
    {
        var info = MakeInfo(
            new UpdateAsset("OmniKeyVault-2.2.0-portable-win-x64.zip", "https://example.com/zip", 50_000_000),
            new UpdateAsset("OmniKeyVault-Setup-2.2.0.exe", "https://example.com/exe", 30_000_000));

        var result = UpdateService.FindInstallerAsset(info);

        result.Should().NotBeNull();
        result!.Name.Should().Be("OmniKeyVault-Setup-2.2.0.exe");
    }

    [Fact]
    public void FindInstallerAsset_FallsBackToAnyExe()
    {
        var info = MakeInfo(
            new UpdateAsset("OmniKeyVault-2.2.0-portable-win-x64.zip", "https://example.com/zip", 50_000_000),
            new UpdateAsset("okv-installer.exe", "https://example.com/exe", 30_000_000));

        var result = UpdateService.FindInstallerAsset(info);

        result.Should().NotBeNull();
        result!.Name.Should().Be("okv-installer.exe");
    }

    [Fact]
    public void FindInstallerAsset_FallsBackToZip()
    {
        var info = MakeInfo(
            new UpdateAsset("OmniKeyVault-2.2.0-portable-win-x64.zip", "https://example.com/zip", 50_000_000),
            new UpdateAsset("README.md", "https://example.com/md", 1_000));

        var result = UpdateService.FindInstallerAsset(info);

        result.Should().NotBeNull();
        result!.Name.Should().Be("OmniKeyVault-2.2.0-portable-win-x64.zip");
    }

    [Fact]
    public void FindInstallerAsset_ReturnsNull_WhenNoAssets()
    {
        var info = MakeInfo();
        var result = UpdateService.FindInstallerAsset(info);
        result.Should().BeNull();
    }

    [Fact]
    public void FindInstallerAsset_PrefersSetupExe_RegardlessOfOrder()
    {
        // Even if .exe without "Setup" appears first, the Setup .exe should win
        var info = MakeInfo(
            new UpdateAsset("some-other.exe", "https://example.com/other", 10_000_000),
            new UpdateAsset("OmniKeyVault-Setup-2.2.0.exe", "https://example.com/setup", 30_000_000),
            new UpdateAsset("portable.zip", "https://example.com/zip", 50_000_000));

        var result = UpdateService.FindInstallerAsset(info);

        result.Should().NotBeNull();
        result!.Name.Should().Be("OmniKeyVault-Setup-2.2.0.exe");
    }

    // ---- DownloadProgress ----

    [Fact]
    public void DownloadProgress_Percentage_CalculatedCorrectly()
    {
        var p = new DownloadProgress(50, 200);
        p.Percentage.Should().Be(25.0);
    }

    [Fact]
    public void DownloadProgress_Percentage_Zero_WhenTotalUnknown()
    {
        var p = new DownloadProgress(100, 0);
        p.Percentage.Should().Be(0.0);
    }

    [Fact]
    public void DownloadProgress_Percentage_100_WhenComplete()
    {
        var p = new DownloadProgress(200, 200);
        p.Percentage.Should().Be(100.0);
    }

    [Fact]
    public void DownloadProgress_Mb_FormattedCorrectly()
    {
        var p = new DownloadProgress(1024 * 1024, 10 * 1024 * 1024);
        p.ReceivedMb.Should().Be("1.0");
        p.TotalMb.Should().Be("10.0");
    }

    // ---- DownloadAssetAsync (integration test with local HTTP listener) ----

    [Fact]
    public async Task DownloadAssetAsync_DownloadsFile_Correctly()
    {
        // Arrange: start a local HTTP listener that serves a known payload
        var payload = new byte[1024 * 100]; // 100 KB
        new Random(42).NextBytes(payload);

        using var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://localhost:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var asset = new UpdateAsset("test-asset.bin", $"{prefix}test-asset.bin", payload.Length);

        // Serve the file asynchronously
        _ = Task.Run(() =>
        {
            try
            {
                var ctx = listener.GetContext();
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.OutputStream.Write(payload, 0, payload.Length);
                ctx.Response.OutputStream.Close();
                ctx.Response.Close();
            }
            catch { /* listener stopped */ }
        });

        try
        {
            // Act
            var downloadedPath = await _svc.DownloadAssetAsync(asset);

            // Assert
            File.Exists(downloadedPath).Should().BeTrue();
            var downloaded = await File.ReadAllBytesAsync(downloadedPath);
            downloaded.Should().Equal(payload);

            // Cleanup
            try { File.Delete(downloadedPath); } catch { }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DownloadAssetAsync_ReportsProgress()
    {
        // Arrange: serve a 50 KB file and verify progress is reported
        var payload = new byte[1024 * 50];
        new Random(123).NextBytes(payload);

        using var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://localhost:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var asset = new UpdateAsset("progress-test.bin", $"{prefix}progress-test.bin", payload.Length);
        var progressReports = new List<DownloadProgress>();

        _ = Task.Run(() =>
        {
            try
            {
                var ctx = listener.GetContext();
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.OutputStream.Write(payload, 0, payload.Length);
                ctx.Response.OutputStream.Close();
                ctx.Response.Close();
            }
            catch { /* listener stopped */ }
        });

        try
        {
            var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));
            var downloadedPath = await _svc.DownloadAssetAsync(asset, progress);

            // Assert: at least one progress report should have been made
            progressReports.Should().NotBeEmpty();
            // The last report should show 100% (or close to it, since total is known)
            progressReports.Last().Percentage.Should().BeGreaterThan(0);

            try { File.Delete(downloadedPath); } catch { }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DownloadAssetAsync_CleansUpOldDownloads()
    {
        // Arrange: create an old fake installer in the temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), "OmniKeyVault-Update");
        Directory.CreateDirectory(tempDir);
        var oldFile = Path.Combine(tempDir, "OmniKeyVault-Setup-0.0.1.exe");
        await File.WriteAllTextAsync(oldFile, "old content");

        var payload = new byte[1024];
        new Random(456).NextBytes(payload);

        using var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://localhost:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var asset = new UpdateAsset("OmniKeyVault-Setup-9.9.9.exe", $"{prefix}OmniKeyVault-Setup-9.9.9.exe", payload.Length);

        _ = Task.Run(() =>
        {
            try
            {
                var ctx = listener.GetContext();
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.OutputStream.Write(payload, 0, payload.Length);
                ctx.Response.OutputStream.Close();
                ctx.Response.Close();
            }
            catch { /* listener stopped */ }
        });

        try
        {
            var downloadedPath = await _svc.DownloadAssetAsync(asset);

            // Assert: the old file should have been deleted
            File.Exists(oldFile).Should().BeFalse();
            // The new file should exist
            File.Exists(downloadedPath).Should().BeTrue();

            try { File.Delete(downloadedPath); } catch { }
        }
        finally
        {
            listener.Stop();
        }
    }

    // ---- LaunchInstaller ----

    [Fact]
    public void LaunchInstaller_ThrowsForNonExistentFile()
    {
        var act = () => UpdateService.LaunchInstaller(Path.Combine(Path.GetTempPath(), "nonexistent-installer-12345.exe"));
        act.Should().Throw<FileNotFoundException>();
    }

    // ---- Helpers ----

    private static UpdateInfo MakeInfo(params UpdateAsset[] assets)
    {
        return new UpdateInfo(
            TagName: "v2.2.0",
            Version: new Version(2, 2, 0),
            Name: "Test Release",
            ReleaseUrl: "https://github.com/mawenshui/omnikey-vault/releases/tag/v2.2.0",
            Body: "Test release body",
            PublishedAt: DateTime.UtcNow,
            Assets: assets.ToList()
        );
    }

    private static int GetFreePort()
    {
        using var sock = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)sock.LocalEndPoint!).Port;
    }
}
