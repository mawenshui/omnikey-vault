using FluentAssertions;
using OmniKeyVault.Contracts;
using OmniKeyVault.Infrastructure;
using Xunit;

namespace OmniKeyVault.Tests.Storage;

/// <summary>
/// Tests for IStorageProvider (atomic writes, file existence, etc.) and ILockProvider.
/// Covers FMT-RECOV-01..03 from OKV_FORMAT.md 搂15.
/// </summary>
public class StorageTests
{
    [Fact]
    public async Task WriteAtomicAsync_WritesFile()
    {
        var sp = new FileSystemStorageProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-store-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "test.bin");
            await sp.WriteAtomicAsync(path, async s =>
            {
                await s.WriteAsync(new byte[] { 1, 2, 3, 4 });
            });
            File.Exists(path).Should().BeTrue();
            (await sp.ReadAllBytesAsync(path)).Should().Equal(new byte[] { 1, 2, 3, 4 });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task WriteAtomicAsync_OverwritesExisting()
    {
        var sp = new FileSystemStorageProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-store-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "test.bin");
            await sp.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
            await sp.WriteAllBytesAsync(path, new byte[] { 4, 5, 6 });
            (await sp.ReadAllBytesAsync(path)).Should().Equal(new byte[] { 4, 5, 6 });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task WriteAtomicAsync_DoesNotLeaveTmpFile()
    {
        var sp = new FileSystemStorageProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-store-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "test.bin");
            await sp.WriteAtomicAsync(path, async s => await s.WriteAsync(new byte[] { 1 }));
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExistsAsync_NonExistentFile_ReturnsFalse()
    {
        var sp = new FileSystemStorageProvider();
        (await sp.ExistsAsync("Z:\\nope\\nope\\nope")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        var sp = new FileSystemStorageProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-store-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "test.bin");
            await sp.WriteAllBytesAsync(path, new byte[] { 1 });
            await sp.DeleteAsync(path);
            File.Exists(path).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DeleteAsync_NonExistentFile_DoesNotThrow()
    {
        var sp = new FileSystemStorageProvider();
        var act = async () => await sp.DeleteAsync("Z:\\nope\\nope");
        await act.Should().NotThrowAsync();
    }

    // ---- FMT-RECOV-01: .okv.tmp cleanup ----
    [Fact]
    public async Task FMT_RECOV_01_TmpFileIsNotLeftAfterNormalWrite()
    {
        // After a normal WriteAsync, .okv.tmp should not exist.
        var sp = new FileSystemStorageProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-recovery-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "vault.okv");
            await sp.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- File lock ----
    [Fact]
    public async Task AcquireLockAsync_CreatesLockFile()
    {
        var lp = new LockProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-lock-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var lockPath = Path.Combine(dir, ".okv.lock");
            using var l = await lp.AcquireLockAsync(lockPath, "test-device");
            File.Exists(lockPath).Should().BeTrue();
            l.LockFilePath.Should().Be(lockPath);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task AcquireLockAsync_DoubleAcquire_WhileFirstHeld_FailsUntilReleased()
    {
        var lp = new LockProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-lock-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var lockPath = Path.Combine(dir, ".okv.lock");
            using var l1 = await lp.AcquireLockAsync(lockPath, "test-device-1");
            // Second acquire should fail because the file is exclusive
            var act = async () => await lp.AcquireLockAsync(lockPath, "test-device-2");
            await act.Should().ThrowAsync<IOException>();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task FileLock_Dispose_RemovesLockFile()
    {
        var lp = new LockProvider();
        var dir = Path.Combine(Path.GetTempPath(), "okv-lock-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        try
        {
            var lockPath = Path.Combine(dir, ".okv.lock");
            var l = await lp.AcquireLockAsync(lockPath, "test-device");
            l.Dispose();
            File.Exists(lockPath).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }
}
