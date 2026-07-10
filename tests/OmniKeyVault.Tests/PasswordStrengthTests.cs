using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests;

/// <summary>
/// §1.3: Tests for PasswordStrength evaluator.
/// Verifies that weak passwords get low scores and are rejected,
/// while strong passwords get high scores and are accepted.
/// </summary>
public class PasswordStrengthTests
{
    [Fact]
    public void Score_EmptyPassword_ReturnsZero()
    {
        PasswordStrength.Score("").Should().Be(0);
        PasswordStrength.Score(null).Should().Be(0);
    }

    [Fact]
    public void Score_VeryWeakPassword_LowScore()
    {
        // Common password
        PasswordStrength.Score("password").Should().BeLessThan(3);
        // Sequential
        PasswordStrength.Score("12345678").Should().BeLessThan(3);
        // Repeated
        PasswordStrength.Score("aaaaaaaa").Should().BeLessThan(3);
    }

    [Fact]
    public void Score_StrongPassword_HighScore()
    {
        // Strong: long + mixed variety
        PasswordStrength.Score("Kx9#mP2$vL7nQ4@w").Should().BeGreaterThanOrEqualTo(3);
        PasswordStrength.Score("Tr@velBr0adly2024!").Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Score_CommonPassword_Penalized()
    {
        // "password123" is a common password + has sequence
        var score = PasswordStrength.Score("password123");
        score.Should().BeLessThan(3, "common passwords should be penalized");
    }

    [Fact]
    public void Score_OnlyDigits_Penalized()
    {
        var score = PasswordStrength.Score("123456789012");
        score.Should().BeLessThan(3, "pure digit passwords should be penalized");
    }

    [Fact]
    public void Score_OnlyLowercase_Penalized()
    {
        var score = PasswordStrength.Score("abcdefghij");
        score.Should().BeLessThan(3, "pure lowercase passwords should be penalized");
    }

    [Fact]
    public void Score_KeyboardWalk_Penalized()
    {
        var score = PasswordStrength.Score("qwerty123456");
        score.Should().BeLessThan(3, "keyboard walks should be penalized");
    }

    [Fact]
    public void ShouldReject_WeakPassword_ReturnsTrue()
    {
        PasswordStrength.ShouldReject("password").Should().BeTrue();
        PasswordStrength.ShouldReject("12345678").Should().BeTrue();
        PasswordStrength.ShouldReject("abc").Should().BeTrue();
    }

    [Fact]
    public void ShouldReject_StrongPassword_ReturnsFalse()
    {
        PasswordStrength.ShouldReject("Kx9#mP2$vL7nQ4@w").Should().BeFalse();
        PasswordStrength.ShouldReject("MyStr0ng#Passw0rd!").Should().BeFalse();
    }

    [Fact]
    public void Label_ReturnsCorrectLabel()
    {
        PasswordStrength.Label(0).Should().Be("极弱");
        PasswordStrength.Label(1).Should().Be("弱");
        PasswordStrength.Label(2).Should().Be("一般");
        PasswordStrength.Label(3).Should().Be("强");
        PasswordStrength.Label(4).Should().Be("极强");
    }

    [Fact]
    public void Suggestion_EmptyPassword_ReturnsPrompt()
    {
        PasswordStrength.Suggestion("").Should().Contain("请输入");
        PasswordStrength.Suggestion(null).Should().Contain("请输入");
    }

    [Fact]
    public void Suggestion_WeakPassword_ReturnsImprovements()
    {
        var suggestion = PasswordStrength.Suggestion("abc");
        suggestion.Should().NotBeEmpty();
        // Should mention length
        suggestion.Should().Contain("12");
    }

    [Fact]
    public void Suggestion_StrongPassword_ReturnsPositiveMessage()
    {
        var suggestion = PasswordStrength.Suggestion("Kx9#mP2$vL7nQ4@w");
        suggestion.Should().Contain("良好");
    }

    [Fact]
    public void Suggestion_NoUppercase_MentionsUppercase()
    {
        var suggestion = PasswordStrength.Suggestion("abcdef123!");
        suggestion.Should().Contain("大写");
    }

    [Fact]
    public void Suggestion_NoDigit_MentionsDigit()
    {
        var suggestion = PasswordStrength.Suggestion("Abcdefgh!");
        suggestion.Should().Contain("数字");
    }

    [Fact]
    public void Suggestion_NoSymbol_MentionsSymbol()
    {
        var suggestion = PasswordStrength.Suggestion("Abcdef123");
        suggestion.Should().Contain("特殊符号");
    }
}
