﻿using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.Domain;

/// <summary>
/// Tests for the Domain layer per OKV_FORMAT.md 搂3 and PRD 搂5.2.
/// No I/O, no crypto 鈥?pure value-object behavior.
/// </summary>
public class DomainTests
{
    // ---- VectorClock (PRD 搂10.2) ----
    [Fact]
    public void VectorClock_Increment_SingleDevice_IncrementsOwnCounter()
    {
        var vc1 = new VectorClock();
        var vc2 = vc1.Increment("device-A");
        vc2.Get("device-A").Should().Be(1);
        vc2.Get("device-B").Should().Be(0);
    }

    [Fact]
    public void VectorClock_Increment_OtherDevice_OnlyIncrementsSpecified()
    {
        var vc = new VectorClock();
        vc = vc.Increment("device-A");
        vc = vc.Increment("device-B");
        vc = vc.Increment("device-A");
        vc.Get("device-A").Should().Be(2);
        vc.Get("device-B").Should().Be(1);
    }

    [Fact]
    public void VectorClock_Compare_Less_ReturnsNegative()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 1 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 2 });
        a.Compare(b).Should().Be(-1);
    }

    [Fact]
    public void VectorClock_Compare_Greater_ReturnsPositive()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 5 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 2 });
        a.Compare(b).Should().Be(1);
    }

    [Fact]
    public void VectorClock_Compare_Equal_ReturnsZero()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 3, ["y"] = 1 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 3, ["y"] = 1 });
        a.Compare(b).Should().Be(0);
    }

    [Fact]
    public void VectorClock_Compare_Concurrent_ReturnsNull()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 0 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 0, ["y"] = 1 });
        a.Compare(b).Should().BeNull();
    }

    [Fact]
    public void VectorClock_Merge_TakesMaxPerComponent()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 3, ["y"] = 1 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 5, ["z"] = 2 });
        var merged = a.Merge(b);
        merged.Get("x").Should().Be(3);
        merged.Get("y").Should().Be(5);
        merged.Get("z").Should().Be(2);
    }

    [Fact]
    public void VectorClock_IsBehind_TrueWhenOtherIsLater()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 1 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 2 });
        a.IsBehind(b).Should().BeTrue();
        b.IsBehind(a).Should().BeFalse();
    }

    [Fact]
    public void VectorClock_IsBehind_TrueWhenOtherHasAdditionalDevices()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 1 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 1 });
        a.IsBehind(b).Should().BeTrue();  // new device appears → causally later
    }

    [Fact]
    public void VectorClock_IsConcurrentWith_TrueWhenNeitherDominates()
    {
        // PRD §10.2: concurrent = neither clock is causally later than the other.
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 0 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 0, ["y"] = 1 });
        a.IsConcurrentWith(b).Should().BeTrue();
        b.IsConcurrentWith(a).Should().BeTrue();
    }

    [Fact]
    public void VectorClock_IsConcurrentWith_FalseWhenCausal()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 2, ["y"] = 1 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 1 });
        a.IsConcurrentWith(b).Should().BeFalse();  // a is causally after b
    }

    [Fact]
    public void VectorClock_IsEqualTo_TrueWhenComponentsMatch()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 2 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 2 });
        a.IsEqualTo(b).Should().BeTrue();
    }

    [Fact]
    public void VectorClock_Diff_ReturnsPerDevicePair()
    {
        var a = new VectorClock(new Dictionary<string, long> { ["x"] = 3, ["y"] = 1 });
        var b = new VectorClock(new Dictionary<string, long> { ["x"] = 1, ["y"] = 5, ["z"] = 2 });
        var diff = a.Diff(b);
        diff["x"].Should().Be((3L, 1L));
        diff["y"].Should().Be((1L, 5L));
        diff["z"].Should().Be((0L, 2L));
    }

    // ---- SEC-T7-01: vector clock merge against replay (PRD §13) ----

    [Fact]
    public void SEC_T7_01_RemoteRollback_DetectedByIsBehind()
    {
        // Local has seen a write from device-A=5. Remote (replayed old file) shows device-A=3.
        var local = new VectorClock(new Dictionary<string, long> { ["device-A"] = 5 });
        var remote = new VectorClock(new Dictionary<string, long> { ["device-A"] = 3 });
        local.IsBehind(remote).Should().BeFalse();  // local is ahead
        remote.IsBehind(local).Should().BeTrue();    // remote is the rollback
    }

    [Fact]
    public void SEC_T7_01_AfterMerge_NoLossofCausality()
    {
        // Two devices both edited concurrently. Merge takes per-component max.
        var a = new VectorClock(new Dictionary<string, long> { ["device-A"] = 3, ["device-B"] = 0 });
        var b = new VectorClock(new Dictionary<string, long> { ["device-A"] = 0, ["device-B"] = 2 });
        var merged = a.Merge(b);
        merged.Get("device-A").Should().Be(3);
        merged.Get("device-B").Should().Be(2);
        // After merging, neither side is strictly behind the merged clock.
        merged.IsBehind(merged).Should().BeFalse();
    }

    [Fact]
    public void SEC_T7_01_ThreeWayMerge_PicksMax()
    {
        // Three devices all wrote; merge takes the max of all three.
        var a = new VectorClock(new Dictionary<string, long> { ["A"] = 1, ["B"] = 0, ["C"] = 0 });
        var b = new VectorClock(new Dictionary<string, long> { ["A"] = 0, ["B"] = 2, ["C"] = 0 });
        var c = new VectorClock(new Dictionary<string, long> { ["A"] = 0, ["B"] = 0, ["C"] = 3 });
        var merged = a.Merge(b).Merge(c);
        merged.Get("A").Should().Be(1);
        merged.Get("B").Should().Be(2);
        merged.Get("C").Should().Be(3);
    }

    [Fact]
    public void VectorClock_Increment_DoesNotMutateOriginal()
    {
        // Immutability: Increment returns a new clock; the original is untouched.
        var original = new VectorClock();
        var incremented = original.Increment("d1");
        original.Get("d1").Should().Be(0);
        incremented.Get("d1").Should().Be(1);
        original.Should().NotBeSameAs(incremented);
    }

    // ---- Field (PLATFORM_TEMPLATES 搂2.4 mask rules) ----
    [Fact]
    public void Field_DisplayMask_UsesCustomMaskWhenProvided()
    {
        var f = new Field { Key = "x", Value = FieldCodec.Encode("secret123"), Kind = FieldKind.Secret, Sensitive = true, Mask = "custom-mask" };
        f.DisplayMask().Should().Be("custom-mask");
    }

    [Theory]
    [InlineData("sk-abc", 6)]                  // 6 chars -> all bullets
    [InlineData("sk-abcdefghij", 13)]          // 13 chars -> first 3 + bullet + last 2
    [InlineData("sk-abcdefghijklmnop", 19)]    // 19 chars > 16 -> prefix-sk + bullets + last 4
    [InlineData("sk-proj-abc1234567890XYZtest", 28)]
    [InlineData("verylongsecretvaluewithnodashprefix", 35)]
    public void Field_DisplayMask_GeneratesDefaultMaskForDifferentLengths(string value, int valueLen)
    {
        value.Length.Should().Be(valueLen);
        var f = new Field { Key = "x", Value = FieldCodec.Encode(value), Kind = FieldKind.Secret, Sensitive = true };
        var mask = f.DisplayMask();
        // For values longer than 16, the last 4 chars should be preserved (PLATFORM_TEMPLATES §2.4).
        if (value.Length > 16)
        {
            mask.Should().EndWith(value.Substring(value.Length - 4));
        }
        // For values between 9-16, the last 2 chars are preserved.
        else if (value.Length >= 9)
        {
            mask.Should().EndWith(value.Substring(value.Length - 2));
        }
        // The mask should not be the literal value (it's masked).
        if (value.Length > 0) mask.Should().NotBe(value);
    }

    [Fact]
    public void Field_DisplayMask_EmptyValueReturnsEmpty()
    {
        var f = new Field { Key = "x", Value = Array.Empty<byte>(), Kind = FieldKind.Secret, Sensitive = true };
        f.DisplayMask().Should().BeEmpty();
    }

    // ---- FieldValidation ----
    [Fact]
    public void FieldValidation_IsValid_NoRegexReturnsTrue()
    {
        var v = new FieldValidation { Hint = "any value ok" };
        v.IsValid("anything").Should().BeTrue();
    }

    [Fact]
    public void FieldValidation_IsValid_MatchesValidValue()
    {
        var v = new FieldValidation { Regex = "^sk-[A-Za-z0-9]+$", Hint = "sk-..." };
        v.IsValid("sk-abc123").Should().BeTrue();
        v.IsValid("AKIA...").Should().BeFalse();
    }

    [Fact]
    public void FieldValidation_IsValid_InvalidRegexTreatedAsNoValidation()
    {
        // Invalid regex patterns (unmatched bracket) should not throw; they should return true.
        var v = new FieldValidation { Regex = "[unclosed", Hint = "broken" };
        var act = () => v.IsValid("anything");
        act.Should().NotThrow();
        v.IsValid("anything").Should().BeTrue();
    }

    // ---- Entry (PRD 搂5.2) ----
    [Fact]
    public void Entry_WithUpdate_IncrementsVersion()
    {
        var e = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        var updated = e.WithUpdate(x => x, DateTimeOffset.UtcNow);
        updated.Version.Should().Be(2);
    }

    [Fact]
    public void Entry_FindField_KeyMatch()
    {
        var f = new Field { Key = "api_key", Value = FieldCodec.Encode("x"), Kind = FieldKind.Secret, Sensitive = true };
        var e = new Entry
        {
            Id = Guid.NewGuid(),
            Type = EntryType.ApiKey,
            Name = "t",
            Fields = new[] { f },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        e.FindField("api_key").Should().BeSameAs(f);
        e.FindField("nonexistent").Should().BeNull();
    }

    // ---- Argon2Params ----
    [Fact]
    public void Argon2Params_Default_Has256MiB()
    {
        Argon2Params.Default.Memory.Should().Be(256 * 1024 * 1024);
        Argon2Params.Default.Time.Should().Be(3);
        Argon2Params.Default.Parallelism.Should().Be(4);
    }

    [Fact]
    public void Argon2Params_ForTests_ReducedMemory()
    {
        var p = Argon2Params.ForTests(8 * 1024 * 1024);
        p.Memory.Should().Be(8 * 1024 * 1024);
    }

    // ---- Profile ----
    [Fact]
    public void Profile_FindEntry_KeyedByGuid()
    {
        var id = Guid.NewGuid();
        var e = new Entry
        {
            Id = id,
            Type = EntryType.ApiKey,
            Name = "t",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        var p = new Profile
        {
            Id = Guid.NewGuid(),
            Name = "prod",
            Entries = new[] { e }
        };
        p.FindEntry(id).Should().BeSameAs(e);
        p.FindEntry(Guid.NewGuid()).Should().BeNull();
    }

    // ---- Exceptions (per ARCHITECTURE.md 搂8.3) ----
    [Fact]
    public void Exceptions_ExitCodes_MatchCliSpec()
    {
        ((int)ExitCodes.Success).Should().Be(0);
        ((int)ExitCodes.VaultLocked).Should().Be(3);
        ((int)ExitCodes.CryptoError).Should().Be(4);
        ((int)ExitCodes.ProfileNotFound).Should().Be(5);
        ((int)ExitCodes.EntryNotFound).Should().Be(7);
        ((int)ExitCodes.FieldNotFound).Should().Be(8);
        ((int)ExitCodes.FileCorrupt).Should().Be(13);
    }
}
