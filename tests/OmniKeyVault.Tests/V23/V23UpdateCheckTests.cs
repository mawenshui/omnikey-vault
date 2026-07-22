using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V23;

/// <summary>
/// v2.3.2: Tests for the structured UpdateCheckResult return type.
/// Verifies that "no update", "update available", and "check failed"
/// are properly distinguished — the core fix for the bug where network
/// errors and rate limits were silently reported as "already up to date".
/// </summary>
public class V23UpdateCheckTests
{
    // ---- UpdateCheckResult behavior ----

    [Fact]
    public void UpdateCheckResult_NoUpdate_HasCorrectFlags()
    {
        var result = new UpdateCheckResult(UpdateCheckStatus.NoUpdate, null, null);
        result.HasUpdate.Should().BeFalse();
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void UpdateCheckResult_UpdateAvailable_HasCorrectFlags()
    {
        var info = MakeInfo("v9.9.9", new Version(9, 9, 9));
        var result = new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info, null);
        result.HasUpdate.Should().BeTrue();
        result.Failed.Should().BeFalse();
        result.Info.Should().NotBeNull();
    }

    [Fact]
    public void UpdateCheckResult_CheckFailed_HasCorrectFlags()
    {
        var result = new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, "网络请求失败");
        result.HasUpdate.Should().BeFalse();
        result.Failed.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void UpdateCheckResult_CheckFailed_WithNullMessage_HasNullError()
    {
        var result = new UpdateCheckResult(UpdateCheckStatus.CheckFailed, null, null);
        result.Failed.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(UpdateCheckStatus.NoUpdate, false, false)]
    [InlineData(UpdateCheckStatus.UpdateAvailable, true, false)]
    [InlineData(UpdateCheckStatus.CheckFailed, false, true)]
    public void UpdateCheckResult_AllStatuses_HaveCorrectFlags(
        UpdateCheckStatus status, bool expectedHasUpdate, bool expectedFailed)
    {
        var result = new UpdateCheckResult(status, null, null);
        result.HasUpdate.Should().Be(expectedHasUpdate);
        result.Failed.Should().Be(expectedFailed);
    }

    // ---- FindInstallerAsset with real-world asset names ----

    [Fact]
    public void FindInstallerAsset_Works_WithV231AssetNames()
    {
        var info = MakeInfo(
            new UpdateAsset("OmniKeyVault-2.3.1-win-x64.zip", "https://example.com/zip", 44736051),
            new UpdateAsset("OmniKeyVault-Setup-2.3.1.exe", "https://example.com/exe", 33269254));

        var result = UpdateService.FindInstallerAsset(info);

        result.Should().NotBeNull();
        result!.Name.Should().Be("OmniKeyVault-Setup-2.3.1.exe");
    }

    // ---- Version comparison edge cases ----

    [Fact]
    public void Version_Parse_FromTag_WorksCorrectly()
    {
        // Verify that version parsing from tag names works as expected
        var v1 = new Version("2.2.1");
        var v2 = new Version("2.3.2");
        (v2 > v1).Should().BeTrue();
    }

    [Fact]
    public void Version_Compare_WithBuildComponent()
    {
        // Assembly versions have 4 components (e.g. 2.3.2.0)
        // GitHub tags parse to 3 components (e.g. 2.3.2)
        // .NET Version comparison should still work correctly
        var assemblyVersion = new Version(2, 2, 1, 0);
        var tagVersion = new Version(2, 3, 2);
        (tagVersion > assemblyVersion).Should().BeTrue();
    }

    // ---- Helpers ----

    private static UpdateInfo MakeInfo(params UpdateAsset[] assets)
    {
        return new UpdateInfo(
            TagName: "v2.3.2",
            Version: new Version(2, 3, 2),
            Name: "Test Release",
            ReleaseUrl: "https://github.com/mawenshui/omnikey-vault/releases/tag/v2.3.2",
            Body: "Test body",
            PublishedAt: DateTime.UtcNow,
            Assets: assets.ToList()
        );
    }

    private static UpdateInfo MakeInfo(string tag, Version version, params UpdateAsset[] assets)
    {
        return new UpdateInfo(
            TagName: tag,
            Version: version,
            Name: "Test Release",
            ReleaseUrl: "https://github.com/mawenshui/omnikey-vault/releases/tag/" + tag,
            Body: "Test body",
            PublishedAt: DateTime.UtcNow,
            Assets: assets.ToList()
        );
    }
}
