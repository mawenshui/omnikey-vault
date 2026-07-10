using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests;

/// <summary>
/// §1.2: Tests for LogRedactor — ensures sensitive field values
/// are masked in log output.
/// </summary>
public class LogRedactorTests
{
    [Fact]
    public void Redact_PasswordFieldValue_ReplacedWithStars()
    {
        var input = "Login attempt: password=secret123 status=ok";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("secret123");
        result.Should().Contain("password=***");
    }

    [Fact]
    public void Redact_ApiKeyFieldValue_ReplacedWithStars()
    {
        var input = "Config loaded: api_key=sk-abc123xyz";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("sk-abc123xyz");
        result.Should().Contain("***");
    }

    [Fact]
    public void Redact_TokenFieldValue_ReplacedWithStars()
    {
        var input = "Auth: token=ghp_abcdef123456";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("ghp_abcdef123456");
        result.Should().Contain("***");
    }

    [Fact]
    public void Redact_ColonSeparator_AlsoRedacted()
    {
        var input = "Debug: secret: my-secret-value";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("my-secret-value");
        result.Should().Contain("***");
    }

    [Fact]
    public void Redact_NoSensitiveFields_PassesThrough()
    {
        var input = "Application started successfully on port 8080";
        var result = LogRedactor.Redact(input);
        result.Should().Be(input);
    }

    [Fact]
    public void Redact_EmptyString_ReturnsEmpty()
    {
        LogRedactor.Redact("").Should().Be("");
        LogRedactor.Redact(null).Should().Be("");
    }

    [Fact]
    public void Redact_MultipleSensitiveFields_AllRedacted()
    {
        var input = "password=pwd123 api_key=sk-test token=ghp_token";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("pwd123");
        result.Should().NotContain("sk-test");
        result.Should().NotContain("ghp_token");
        result.Should().Contain("***");
    }

    [Fact]
    public void Redact_KnownSecretPrefix_Masked()
    {
        var input = "Using key: sk-proj-abcdef123456789";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("sk-proj-abcdef123456789");
    }

    [Fact]
    public void Redact_AwsKeyPrefix_Masked()
    {
        var input = "Credential: AKIAIOSFODNN7EXAMPLE";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("AKIAIOSFODNN7EXAMPLE");
    }

    [Fact]
    public void Redact_PrivateKeyBlock_Masked()
    {
        // PEM headers contain spaces so the prefix matcher won't catch the full block,
        // but a private_key= value will be caught by the field-name matcher.
        var input = "Loaded private_key=MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSj";
        var result = LogRedactor.Redact(input);
        result.Should().NotContain("MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSj");
        result.Should().Contain("***");
    }

    [Fact]
    public void IsSensitiveFieldName_Password_ReturnsTrue()
    {
        LogRedactor.IsSensitiveFieldName("password").Should().BeTrue();
        LogRedactor.IsSensitiveFieldName("api_key").Should().BeTrue();
        LogRedactor.IsSensitiveFieldName("master_key").Should().BeTrue();
        LogRedactor.IsSensitiveFieldName("token").Should().BeTrue();
    }

    [Fact]
    public void IsSensitiveFieldName_NonSensitive_ReturnsFalse()
    {
        LogRedactor.IsSensitiveFieldName("name").Should().BeFalse();
        LogRedactor.IsSensitiveFieldName("port").Should().BeFalse();
        LogRedactor.IsSensitiveFieldName("").Should().BeFalse();
    }
}
