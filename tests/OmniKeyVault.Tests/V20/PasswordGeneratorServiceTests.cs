using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for PasswordGeneratorService: password generation, passphrase generation,
/// and strength estimation.
/// </summary>
public class PasswordGeneratorServiceTests
{
    private readonly PasswordGeneratorService _svc = new();

    [Fact]
    public void Generate_DefaultLength_Is20()
    {
        var pwd = _svc.Generate();
        pwd.Should().HaveLength(20);
    }

    [Fact]
    public void Generate_CustomLength_ReturnsCorrectLength()
    {
        _svc.Generate(8).Should().HaveLength(8);
        _svc.Generate(32).Should().HaveLength(32);
        _svc.Generate(64).Should().HaveLength(64);
    }

    [Fact]
    public void Generate_MinLengthClampedTo4()
    {
        _svc.Generate(1).Should().HaveLength(4);
        _svc.Generate(0).Should().HaveLength(4);
        _svc.Generate(-5).Should().HaveLength(4);
    }

    [Fact]
    public void Generate_MaxLengthClampedTo128()
    {
        _svc.Generate(200).Should().HaveLength(128);
    }

    [Fact]
    public void Generate_OnlyUppercase_ContainsOnlyUppercase()
    {
        var pwd = _svc.Generate(20, useUppercase: true, useLowercase: false, useDigits: false, useSymbols: false);
        pwd.Should().HaveLength(20);
        pwd.Should().Match(c => c.All(char.IsUpper), "all characters should be uppercase");
    }

    [Fact]
    public void Generate_OnlyDigits_ContainsOnlyDigits()
    {
        var pwd = _svc.Generate(20, useUppercase: false, useLowercase: false, useDigits: true, useSymbols: false);
        pwd.Should().HaveLength(20);
        pwd.Should().Match(c => c.All(char.IsDigit), "all characters should be digits");
    }

    [Fact]
    public void Generate_ExcludeAmbiguous_NoAmbiguousChars()
    {
        // The excludeAmbiguous mode excludes I, O, l, o, 0, 1 from the character pools
        var excludedChars = "IlOo01";
        for (int i = 0; i < 50; i++)
        {
            var pwd = _svc.Generate(30, excludeAmbiguous: true);
            foreach (var c in excludedChars)
                pwd.Should().NotContain(c.ToString(), $"ambiguous character '{c}' should be excluded");
        }
    }

    [Fact]
    public void Generate_AllCharSetsDisabled_FallsBackToLowercase()
    {
        var pwd = _svc.Generate(10, useUppercase: false, useLowercase: false, useDigits: false, useSymbols: false);
        pwd.Should().HaveLength(10);
        pwd.Should().Match(c => c.All(char.IsLetter), "should fall back to lowercase letters");
    }

    [Fact]
    public void Generate_TwoCalls_ProduceDifferentPasswords()
    {
        var pwd1 = _svc.Generate(32);
        var pwd2 = _svc.Generate(32);
        pwd1.Should().NotBe(pwd2, "two consecutive calls should produce different passwords");
    }

    [Fact]
    public void Generate_IncludesAtLeastOneFromEachSelectedPool()
    {
        var pwd = _svc.Generate(4, useUppercase: true, useLowercase: true, useDigits: true, useSymbols: true);
        pwd.Should().HaveLength(4);
        pwd.Any(char.IsUpper).Should().BeTrue("at least one uppercase");
        pwd.Any(char.IsLower).Should().BeTrue("at least one lowercase");
        pwd.Any(char.IsDigit).Should().BeTrue("at least one digit");
        pwd.Any(c => !char.IsLetterOrDigit(c)).Should().BeTrue("at least one symbol");
    }

    [Fact]
    public void GeneratePassphrase_Default4Words_SeparatedByDash()
    {
        var phrase = _svc.GeneratePassphrase();
        var parts = phrase.Split('-');
        parts.Should().HaveCount(4, "default passphrase should have 4 words");
        parts.Should().OnlyContain(p => p.Length > 0, "all parts should be non-empty");
    }

    [Fact]
    public void GeneratePassphrase_CustomWordCount()
    {
        var phrase = _svc.GeneratePassphrase(6, "_");
        var parts = phrase.Split('_');
        parts.Should().HaveCount(6, "should have 6 words with custom separator");
    }

    [Fact]
    public void GeneratePassphrase_CustomSeparator()
    {
        var phrase = _svc.GeneratePassphrase(3, ".");
        phrase.Should().Contain(".", "should use custom separator");
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 0)]
    [InlineData("abcdefgh", 1)]
    [InlineData("abcdefghij", 1)]
    [InlineData("abcdefghijkl", 2)]
    [InlineData("abcdefghijklmnop", 3)]
    [InlineData("Abcdefghijklmnop1234!@#$", 4)]
    [InlineData("Abcdefghijklmnop1234!@#$xyz", 4)]
    public void EstimateStrength_VariousPasswords_ReturnsExpectedScore(string password, int expectedMinScore)
    {
        var score = PasswordGeneratorService.EstimateStrength(password);
        score.Should().BeGreaterThanOrEqualTo(expectedMinScore,
            $"password of length {password.Length} should have at least score {expectedMinScore}");
        score.Should().BeInRange(0, 4);
    }

    [Theory]
    [InlineData(0, "极弱")]
    [InlineData(1, "弱")]
    [InlineData(2, "中等")]
    [InlineData(3, "强")]
    [InlineData(4, "极强")]
    public void StrengthLabel_ReturnsCorrectLabel(int score, string expectedLabel)
    {
        PasswordGeneratorService.StrengthLabel(score).Should().Be(expectedLabel);
    }

    [Fact]
    public void StrengthLabel_InvalidScore_ReturnsUnknown()
    {
        PasswordGeneratorService.StrengthLabel(-1).Should().Be("未知");
        PasswordGeneratorService.StrengthLabel(5).Should().Be("未知");
    }
}
