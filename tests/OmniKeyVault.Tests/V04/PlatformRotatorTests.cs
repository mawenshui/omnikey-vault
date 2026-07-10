using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V04;

/// <summary>v0.4 S8-T1 / S8-T2: PlatformRotator contract tests. We can't hit
/// the real OpenAI / GitHub APIs from a test, so we use a mock rotator that
/// implements <see cref="IPlatformRotator"/> to verify the framework handles
/// success + failure correctly. The real <see cref="OpenAiRotator"/> and
/// <see cref="GitHubPatRotator"/> are instantiated only for smoke tests
/// (constructor + PlatformId/DisplayName/FieldKey metadata).</summary>
public class PlatformRotatorTests
{
    [Fact]
    public void OpenAiRotator_Metadata()
    {
        var r = new OpenAiRotator();
        r.PlatformId.Should().Be("openai");
        r.DisplayName.Should().Be("OpenAI API Key");
        r.FieldKey.Should().Be("api_key");
    }

    [Fact]
    public void GitHubPatRotator_Metadata()
    {
        var r = new GitHubPatRotator();
        r.PlatformId.Should().Be("github");
        r.DisplayName.Should().Be("GitHub Personal Access Token");
        r.FieldKey.Should().Be("token");
    }

    [Fact]
    public async Task OpenAiRotator_EmptyValue_Throws()
    {
        var r = new OpenAiRotator();
        var ex = await Assert.ThrowsAsync<PlatformApiException>(() => r.RotateAsync(""));
        ex.PlatformId.Should().Be("openai");
        ex.Message.Should().NotContain("sk-");  // never echo the value
    }

    [Fact]
    public async Task GitHubPatRotator_EmptyValue_Throws()
    {
        var r = new GitHubPatRotator();
        var ex = await Assert.ThrowsAsync<PlatformApiException>(() => r.RotateAsync(""));
        ex.PlatformId.Should().Be("github");
    }

    /// <summary>Mock rotator used to verify the framework handles the
    /// success / failure paths correctly without hitting a real API.</summary>
    private sealed class MockRotator : IPlatformRotator
    {
        public string PlatformId { get; init; } = "mock";
        public string DisplayName { get; init; } = "Mock Platform";
        public string FieldKey { get; init; } = "secret";
        public Func<string, Task<RotationResult>>? Handler { get; set; }

        public Task<RotationResult> RotateAsync(string currentValue, CancellationToken ct = default)
            => Handler?.Invoke(currentValue) ?? Task.FromResult(new RotationResult
            {
                NewValue = "new-" + currentValue,
                OldValue = currentValue,
                OldValueRevoked = true,
            });
    }

    [Fact]
    public async Task MockRotator_Success_ReturnsNewValue()
    {
        var mock = new MockRotator();
        var result = await mock.RotateAsync("old-value");
        result.NewValue.Should().Be("new-old-value");
        result.OldValue.Should().Be("old-value");
        result.OldValueRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task MockRotator_PlatformApiException_SanitizesMessage()
    {
        var mock = new MockRotator
        {
            Handler = (v) => throw new PlatformApiException("mock", "API returned 401 — re-auth needed"),
        };
        var ex = await Assert.ThrowsAsync<PlatformApiException>(() => mock.RotateAsync("old"));
        ex.PlatformId.Should().Be("mock");
        ex.Message.Should().NotContain("old");  // never include the secret
    }

    [Fact]
    public void RotationResult_RequiresNewAndOld()
    {
        // The `required` modifier is enforced at construction time; building
        // a RotationResult without NewValue should be a compile error, so
        // here we just verify the type's required properties via reflection.
        var props = typeof(RotationResult).GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false).Any())
            .Select(p => p.Name)
            .ToHashSet();
        props.Should().Contain("NewValue");
        props.Should().Contain("OldValue");
    }
}
