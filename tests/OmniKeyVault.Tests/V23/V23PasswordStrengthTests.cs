using Xunit;
using FluentAssertions;
using OmniKeyVault.Application;

namespace OmniKeyVault.Tests.V23;

/// <summary>
/// v2.3: Tests for password strength estimation, which is used in the
/// editor's real-time password strength indicator and the security health report.
/// </summary>
public class V23PasswordStrengthTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 0)]
    [InlineData("password", 0)]
    [InlineData("12345678", 0)]
    [InlineData("Password1", 1)]
    [InlineData("Password1!", 2)]
    [InlineData("MyVerySecureP@ssw0rd2024!", 4)]
    [InlineData("xK9#mQ2$vL7!nR4@", 4)]
    public void EstimateStrength_ReturnsExpectedScore(string password, int minScore)
    {
        var score = PasswordGeneratorService.EstimateStrength(password);
        score.Should().BeGreaterThanOrEqualTo(minScore);
    }

    [Theory]
    [InlineData(0, "极弱")]
    [InlineData(1, "弱")]
    [InlineData(2, "中等")]
    [InlineData(3, "强")]
    [InlineData(4, "极强")]
    public void StrengthLabel_ReturnsCorrectLabel(int score, string expected)
    {
        var label = PasswordGeneratorService.StrengthLabel(score);
        label.Should().Be(expected);
    }

    [Fact]
    public void EstimateStrength_WeakPassword_HasLowScore()
    {
        var score = PasswordGeneratorService.EstimateStrength("password");
        score.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void EstimateStrength_StrongPassword_HasHighScore()
    {
        var score = PasswordGeneratorService.EstimateStrength("MyV3ryStr0ng!P@ssw0rd#2024");
        score.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void EstimateStrength_EmptyString_ReturnsZero()
    {
        var score = PasswordGeneratorService.EstimateStrength("");
        score.Should().Be(0);
    }

    [Fact]
    public void PasswordGenerator_GeneratesPasswordOfCorrectLength()
    {
        var gen = new PasswordGeneratorService();
        var pwd = gen.Generate(32, true, true, true, true, false);
        pwd.Should().HaveLength(32);
    }

    [Fact]
    public void PasswordGenerator_GeneratesUniquePasswords()
    {
        var gen = new PasswordGeneratorService();
        var pwd1 = gen.Generate(20, true, true, true, true, false);
        var pwd2 = gen.Generate(20, true, true, true, true, false);
        pwd1.Should().NotBe(pwd2);
    }

    [Fact]
    public void PasswordGenerator_ExcludesAmbiguousWhenRequested()
    {
        var gen = new PasswordGeneratorService();
        var pwd = gen.Generate(50, true, true, true, true, true);
        // With noAmbiguous=true, these characters should not appear
        pwd.Should().NotContain("I");
        pwd.Should().NotContain("l");
        pwd.Should().NotContain("1");
        pwd.Should().NotContain("O");
        pwd.Should().NotContain("0");
    }
}
