namespace OmniKeyVault.Domain;

/// <summary>
/// Vector clock for multi-device sync conflict detection per PRD §10.2
/// and OKV_FORMAT.md §3.7. Each device maintains its own monotonic counter;
/// merge takes the max per component.
/// </summary>
public sealed class VectorClock
{
    private readonly Dictionary<string, long> _counters;

    public VectorClock() : this(new Dictionary<string, long>()) { }

    public VectorClock(IReadOnlyDictionary<string, long> counters)
    {
        _counters = new Dictionary<string, long>(counters);
    }

    public IReadOnlyDictionary<string, long> Counters => _counters;

    /// <summary>
    /// Returns a new VectorClock with the given device's counter incremented by 1.
    /// </summary>
    public VectorClock Increment(string deviceId)
    {
        var next = new Dictionary<string, long>(_counters);
        next[deviceId] = (next.TryGetValue(deviceId, out var v) ? v : 0) + 1;
        return new VectorClock(next);
    }

    /// <summary>
    /// Returns a new VectorClock that is the per-component max of this and <paramref name="other"/>.
    /// </summary>
    public VectorClock Merge(VectorClock other)
    {
        var merged = new Dictionary<string, long>(_counters);
        foreach (var (k, v) in other._counters)
        {
            if (!merged.TryGetValue(k, out var existing) || v > existing)
                merged[k] = v;
        }
        return new VectorClock(merged);
    }

    /// <summary>
    /// Compares this clock with <paramref name="other"/>:
    ///   -1 if this &lt; other (other is causally later)
    ///    0 if equal
    ///    1 if this &gt; other (this is causally later)
    ///  null if concurrent (neither dominates)
    /// </summary>
    public int? Compare(VectorClock other)
    {
        bool thisLe = true;   // this <= other componentwise
        bool otherLe = true;  // other <= this componentwise

        var allKeys = _counters.Keys.Concat(other._counters.Keys).Distinct();
        foreach (var k in allKeys)
        {
            long a = _counters.TryGetValue(k, out var va) ? va : 0;
            long b = other._counters.TryGetValue(k, out var vb) ? vb : 0;
            if (a > b) thisLe = false;   // this > other at this key → this is NOT <= other
            if (a < b) otherLe = false;  // other > this at this key → other is NOT <= this
        }

        if (thisLe && otherLe) return 0;
        if (thisLe) return -1;
        if (otherLe) return 1;
        return null;
    }

    /// <summary>
    /// Returns the counter for a device, or 0 if absent.
    /// </summary>
    public long Get(string deviceId)
        => _counters.TryGetValue(deviceId, out var v) ? v : 0;

    /// <summary>
    /// True if this clock is strictly behind <paramref name="other"/> (i.e., other is causally later).
    /// Used by sync merge to detect rollback attempts (PRD §10.2 / threat T7).
    /// </summary>
    public bool IsBehind(VectorClock other)
        => Compare(other) == -1;

    /// <summary>
    /// True if the two clocks are concurrent (neither dominates the other).
    /// When this is the case, <see cref="Merge"/> is required to resolve.
    /// </summary>
    public bool IsConcurrentWith(VectorClock other)
        => Compare(other) is null;

    /// <summary>
    /// True if the two clocks are equal (all components match).
    /// </summary>
    public bool IsEqualTo(VectorClock other)
        => Compare(other) == 0;

    /// <summary>
    /// Returns the difference between two clocks, useful for logging sync events.
    /// </summary>
    public IReadOnlyDictionary<string, (long Local, long Remote)> Diff(VectorClock other)
    {
        var allKeys = _counters.Keys.Concat(other._counters.Keys).Distinct();
        var dict = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        foreach (var k in allKeys)
        {
            var l = _counters.TryGetValue(k, out var lv) ? lv : 0;
            var r = other._counters.TryGetValue(k, out var rv) ? rv : 0;
            dict[k] = (l, r);
        }
        return dict;
    }

    public override string ToString()
        => string.Join(", ", _counters.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
}
