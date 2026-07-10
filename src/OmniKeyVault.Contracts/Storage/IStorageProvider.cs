namespace OmniKeyVault.Contracts;

/// <summary>
/// Storage provider interface per ARCHITECTURE.md §6.2.
/// All writes are atomic: write temp file -> fsync -> rename.
/// </summary>
public interface IStorageProvider
{
    Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);
    Task WriteAtomicAsync(string path, Func<Stream, Task> writer, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default);
}

/// <summary>
/// Cross-process file lock per OKV_FORMAT.md §9.
/// IDisposable — releasing the lock deletes the lock file.
/// </summary>
public interface IFileLock : IDisposable
{
    string LockFilePath { get; }
    int ProcessId { get; }
}

/// <summary>
/// File lock provider — atomic acquire via lock-file create.
/// </summary>
public interface ILockProvider
{
    Task<IFileLock> AcquireLockAsync(string lockFilePath, string deviceId, CancellationToken ct = default);
    bool IsLockStale(string lockFilePath);
    void ClearStaleLock(string lockFilePath);
}

/// <summary>
/// Clipboard provider — abstracted for testability. Real implementation
/// uses OS clipboard APIs and clears after a timeout (PRD §5.11).
/// </summary>
public interface IClipboardProvider : IDisposable
{
    void Copy(string text, int clearAfterSeconds = 8);
    event EventHandler? Cleared;
    string? CurrentContent { get; }
    /// <summary>Forces immediate clear of the clipboard.</summary>
    void ClearNow();
}
