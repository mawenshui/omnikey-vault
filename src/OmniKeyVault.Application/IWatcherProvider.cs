using System.Collections.Concurrent;

namespace OmniKeyVault.Application;

/// <summary>
/// S4-T1: abstraction for filesystem event watching. The default
/// implementation uses <see cref="FileSystemWatcher"/>; tests use the
/// in-memory <see cref="InMemoryWatcherProvider"/> to deterministically
/// raise events without touching the disk.
///
/// Debounce: multiple change events on the same path within
/// <see cref="DebounceMs"/> are coalesced into a single notification, with
/// 200ms being the default per the ROADMAP S4-T1 spec.
/// </summary>
public interface IWatcherProvider : IDisposable
{
    /// <summary>The debounce window in milliseconds. Default 200.</summary>
    int DebounceMs { get; set; }

    /// <summary>Start watching a directory. Multiple paths can be watched
    /// simultaneously. Calling Watch a second time on the same path is a
    /// no-op (the prior subscription is kept).</summary>
    void Watch(string directoryPath, string filter = "*.okv");

    /// <summary>Stop watching a directory.</summary>
    void Unwatch(string directoryPath);

    /// <summary>Event raised when a watched file changes (after debounce).
    /// The argument is the full path of the changed file.</summary>
    event EventHandler<string>? FileChanged;
}

/// <summary>Default FileSystemWatcher-based implementation.</summary>
public sealed class FileSystemWatcherProvider : IWatcherProvider
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, System.Timers.Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public int DebounceMs { get; set; } = 200;

    public event EventHandler<string>? FileChanged;

    public void Watch(string directoryPath, string filter = "*.okv")
    {
        lock (_gate)
        {
            if (_watchers.ContainsKey(directoryPath)) return;
            if (!System.IO.Directory.Exists(directoryPath)) return;
            var w = new FileSystemWatcher(directoryPath, filter)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };
            w.Changed += (s, e) => ScheduleDebounced(e.FullPath);
            w.Created += (s, e) => ScheduleDebounced(e.FullPath);
            w.Deleted += (s, e) => ScheduleDebounced(e.FullPath);
            w.Renamed += (s, e) => ScheduleDebounced(e.FullPath);
            _watchers[directoryPath] = w;
        }
    }

    public void Unwatch(string directoryPath)
    {
        lock (_gate)
        {
            if (_watchers.TryRemove(directoryPath, out var w))
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            if (_debounceTimers.TryRemove(directoryPath, out var t))
            {
                t.Stop();
                t.Dispose();
            }
        }
    }

    private void ScheduleDebounced(string path)
    {
        var key = path;
        _debounceTimers.AddOrUpdate(key,
            _ =>
            {
                var newT = new System.Timers.Timer(DebounceMs) { AutoReset = false };
                newT.Elapsed += (_, _) =>
                {
                    System.Timers.Timer? _ = null;
                    _debounceTimers.TryRemove(key, out _);
                    FileChanged?.Invoke(this, key);
                };
                newT.Start();
                return newT;
            },
            (_, existing) =>
            {
                existing.Stop();
                existing.Start();
                return existing;
            });
    }

    public void Dispose()
    {
        foreach (var w in _watchers.Values)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
        foreach (var t in _debounceTimers.Values)
        {
            t.Stop();
            t.Dispose();
        }
        _debounceTimers.Clear();
    }
}

/// <summary>In-memory watcher for tests. Allows deterministic event raising
/// without disk I/O. Each Watch creates an entry; RaiseChange fires the
/// FileChanged event for the given path.</summary>
public sealed class InMemoryWatcherProvider : IWatcherProvider
{
    private readonly HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    public int DebounceMs { get; set; } = 200;
    public event EventHandler<string>? FileChanged;

    public void Watch(string directoryPath, string filter = "*.okv") => _watched.Add(directoryPath);
    public void Unwatch(string directoryPath) => _watched.Remove(directoryPath);

    public void RaiseChange(string filePath) => FileChanged?.Invoke(this, filePath);

    public bool IsWatching(string directoryPath) => _watched.Contains(directoryPath);

    public void Dispose() { }
}
