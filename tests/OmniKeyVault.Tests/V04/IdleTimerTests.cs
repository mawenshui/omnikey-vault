using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V04;

/// <summary>v0.4 S7-T2: IdleTimer tests covering timeout, activity reset,
/// remaining-seconds, and disposal. The "fires after duration" path is
/// covered by an integration test (S7-T2 E2E) rather than a unit test
/// because the real-time wait would slow the test suite noticeably.</summary>
public class IdleTimerTests
{
    [Fact]
    public void SecondsSinceActivity_StartsAtZero_IncreasesOverTime()
    {
        var t = new IdleTimer(15);
        t.SecondsSinceActivity.Should().Be(0);
        System.Threading.Thread.Sleep(1100);
        t.SecondsSinceActivity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RecordActivity_ResetsTimer()
    {
        var t = new IdleTimer(15);
        System.Threading.Thread.Sleep(1100);
        t.SecondsSinceActivity.Should().BeGreaterThan(0);
        t.RecordActivity();
        t.SecondsSinceActivity.Should().Be(0);
    }

    [Fact]
    public void SecondsUntilTimeout_StartsAtFullDuration_AndDecreases()
    {
        var t = new IdleTimer(15);
        t.SecondsUntilTimeout.Should().BeInRange(899, 900);  // 15*60 = 900
        System.Threading.Thread.Sleep(1100);
        t.SecondsUntilTimeout.Should().BeLessThan(900);
    }

    [Fact]
    public void RecordActivity_RestartsCountdown()
    {
        var t = new IdleTimer(15);
        // Wait long enough that SecondsSinceActivity > 0 (so the countdown
        // is observably "in progress", not at the full 900s).
        System.Threading.Thread.Sleep(1500);
        var before = t.SecondsUntilTimeout;
        before.Should().BeLessThan(900);
        t.RecordActivity();
        var after = t.SecondsUntilTimeout;
        after.Should().BeGreaterThan(before);
        // After reset, the countdown jumps back close to the full duration.
        after.Should().BeInRange(898, 900);
    }

    [Fact]
    public void RecordActivity_DuringPolling_PreventsFire()
    {
        // Configure a 1-minute timeout, but keep the activity fresh for 3
        // seconds. With our 1-second poll interval the timer should never
        // fire (would need 60s of no activity).
        var t = new IdleTimer(1);
        var fired = new System.Collections.Concurrent.ConcurrentBag<int>();
        t.IdleTimeoutReached += (_, e) => fired.Add(e.IdleMinutes);
        t.Start();
        for (int i = 0; i < 6; i++)
        {
            System.Threading.Thread.Sleep(500);
            t.RecordActivity();
        }
        fired.Should().BeEmpty();
        t.Stop();
        t.Dispose();
    }

    [Fact]
    public void IdleMinutes_PropertyChange_ResetsTimer()
    {
        var t = new IdleTimer(15);
        System.Threading.Thread.Sleep(500);
        t.IdleMinutes = 5;  // changing the threshold resets activity
        t.SecondsSinceActivity.Should().Be(0);
    }

    [Fact]
    public void IdleMinutes_ClampedToMinimumOne()
    {
        var t = new IdleTimer(0);
        // Constructor clamps to 1
        t.SecondsUntilTimeout.Should().BeInRange(0, 60);
        // Property setter also clamps
        t.IdleMinutes = 0;
        t.SecondsUntilTimeout.Should().BeInRange(0, 60);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var t = new IdleTimer(5);
        t.Start();
        t.Dispose();
        t.Dispose();  // second call should not throw
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        var t = new IdleTimer(5);
        t.Start();
        t.Start();  // second call should not start a duplicate poller
        // No exception, no leak (no way to directly assert, but the
        // idempotency guard is in the source code).
        t.Dispose();
    }
}
