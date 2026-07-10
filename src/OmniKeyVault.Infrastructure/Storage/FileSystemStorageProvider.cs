using OmniKeyVault.Contracts;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// File-system storage provider with atomic writes (tmp + fsync + rename).
/// Per ARCHITECTURE.md §6.2 / OKV_FORMAT.md §10.3.
/// </summary>
public sealed class FileSystemStorageProvider : IStorageProvider
{
    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}", path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
    }

    public async Task WriteAtomicAsync(string path, Func<Stream, Task> writer, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";

        // Open tmp with exclusive access; ensure no other process reads partial state.
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await writer(fs);
            await fs.FlushAsync(ct);
            fs.Flush(flushToDisk: true);  // fsync
        }

        // Atomic replace. On Windows, File.Move with overwrite=true uses MoveFileEx(REPLACE_EXISTING).
        if (File.Exists(path))
            File.Replace(tmp, path, destinationBackupFileName: null);
        else
            File.Move(tmp, path);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(File.Exists(path));

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}", path);
        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        await WriteAtomicAsync(path, async s =>
        {
            await s.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
        }, ct);
    }
}
