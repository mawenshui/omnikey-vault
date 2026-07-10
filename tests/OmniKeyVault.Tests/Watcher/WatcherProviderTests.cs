using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.Watcher;

/// <summary>
/// Tests for IWatcherProvider (S4-T1) using the in-memory variant
/// (deterministic, no disk I/O). The FileSystemWatcher implementation
/// is exercised separately by integration smoke tests on real files.
/// </summary>
public class WatcherProviderTests
{
    [Fact]
    public void InMemory_Watch_TracksPaths()
    {
        using var w = new InMemoryWatcherProvider();
        w.Watch(@"C:\Vaults\A");
        w.Watch(@"C:\Vaults\B", "*.json");
        w.IsWatching(@"C:\Vaults\A").Should().BeTrue();
        w.IsWatching(@"C:\Vaults\B").Should().BeTrue();
        w.IsWatching(@"C:\Vaults\C").Should().BeFalse();
    }

    [Fact]
    public void InMemory_Unwatch_StopsTracking()
    {
        using var w = new InMemoryWatcherProvider();
        w.Watch(@"C:\Vaults\A");
        w.Unwatch(@"C:\Vaults\A");
        w.IsWatching(@"C:\Vaults\A").Should().BeFalse();
    }

    [Fact]
    public void InMemory_RaiseChange_FiresEventWithPath()
    {
        using var w = new InMemoryWatcherProvider();
        string? received = null;
        w.FileChanged += (_, p) => received = p;
        w.RaiseChange(@"C:\Vaults\A\vault.okv");
        received.Should().Be(@"C:\Vaults\A\vault.okv");
    }

    [Fact]
    public void InMemory_MultipleSubscribers_AllFire()
    {
        using var w = new InMemoryWatcherProvider();
        int count = 0;
        w.FileChanged += (_, _) => Interlocked.Increment(ref count);
        w.FileChanged += (_, _) => Interlocked.Increment(ref count);
        w.RaiseChange(@"X");
        count.Should().Be(2);
    }

    [Fact]
    public void InMemory_DebounceMs_IsConfigurable()
    {
        using var w = new InMemoryWatcherProvider { DebounceMs = 500 };
        w.DebounceMs.Should().Be(500);
    }

    [Fact]
    public void InMemory_UnwatchWithoutWatch_IsNoop()
    {
        using var w = new InMemoryWatcherProvider();
        w.Invoking(x => x.Unwatch(@"C:\NeverWatched")).Should().NotThrow();
    }

    [Fact]
    public async Task InMemory_FireMultipleEvents_AllDelivered()
    {
        using var w = new InMemoryWatcherProvider();
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        w.FileChanged += (_, p) => seen.Add(p);
        await Task.Run(() =>
        {
            for (int i = 0; i < 5; i++)
                w.RaiseChange($@"C:\Vaults\file{i}.okv");
        });
        seen.Should().HaveCount(5);
    }
}
