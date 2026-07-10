using System.Diagnostics;
using System.Text.Json;
using OmniKeyVault.Contracts;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// Cross-process file lock via atomic lock-file creation (OKV_FORMAT.md §9.3).
/// Lock file is JSON: { pid, hostname, device_id, ts, app_version }.
/// </summary>
public sealed class FileLock : IFileLock
{
    public string LockFilePath { get; }
    public int ProcessId { get; }
    private readonly FileStream _holdStream;

    internal FileLock(string path, int pid, FileStream holdStream)
    {
        LockFilePath = path;
        ProcessId = pid;
        _holdStream = holdStream;
    }

    public void Dispose()
    {
        try { _holdStream.Dispose(); } catch { }
        try { if (File.Exists(LockFilePath)) File.Delete(LockFilePath); } catch { }
    }
}

public sealed class LockProvider : ILockProvider
{
    public async Task<IFileLock> AcquireLockAsync(string lockFilePath, string deviceId, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(lockFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Try to atomically create the lock file. FileShare.None + CreateNew gives
        // us cross-process exclusion on Windows (OKV_FORMAT.md §9.3).
        var info = new LockInfo
        {
            Pid = Environment.ProcessId,
            Hostname = Environment.MachineName,
            DeviceId = deviceId,
            Ts = DateTimeOffset.UtcNow,
            AppVersion = "0.1.0"
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(info);

        FileStream fs;
        try
        {
            fs = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256, useAsync: true);
            await fs.WriteAsync(json.AsMemory(0, json.Length), ct);
            await fs.FlushAsync(ct);
        }
        catch (IOException) when (IsLockStale(lockFilePath))
        {
            ClearStaleLock(lockFilePath);
            fs = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256, useAsync: true);
            await fs.WriteAsync(json.AsMemory(0, json.Length), ct);
            await fs.FlushAsync(ct);
        }

        return new FileLock(lockFilePath, Environment.ProcessId, fs);
    }

    public bool IsLockStale(string lockFilePath)
    {
        if (!File.Exists(lockFilePath)) return false;
        try
        {
            var json = File.ReadAllBytes(lockFilePath);
            var info = JsonSerializer.Deserialize<LockInfo>(json);
            if (info == null) return true;

            // Check if the PID is still alive. If not, the lock is stale.
            try
            {
                var proc = Process.GetProcessById(info.Pid);
                if (proc == null) return true;
                // Confirm hostname matches — same PID on a different host is also stale.
                if (!string.Equals(proc.MachineName, ".", StringComparison.Ordinal) &&
                    !string.Equals(info.Hostname, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    // Cross-machine PID — consider stale only if process is not reachable.
                    return false;
                }
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
        catch
        {
            return true;
        }
    }

    public void ClearStaleLock(string lockFilePath)
    {
        try { if (File.Exists(lockFilePath)) File.Delete(lockFilePath); } catch { }
    }

    private sealed class LockInfo
    {
        public int Pid { get; set; }
        public string? Hostname { get; set; }
        public string? DeviceId { get; set; }
        public DateTimeOffset Ts { get; set; }
        public string? AppVersion { get; set; }
    }
}
