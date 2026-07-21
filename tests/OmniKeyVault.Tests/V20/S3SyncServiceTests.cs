using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for S3SyncService: configuration validation, URL building,
/// and push/pull error handling without actual network calls.
/// </summary>
public class S3SyncServiceTests : IClassFixture<TempVaultDir>
{
    private readonly TempVaultDir _dir;

    public S3SyncServiceTests(TempVaultDir dir) => _dir = dir;

    [Fact]
    public void IsConfigured_AllFieldsSet_ReturnsTrue()
    {
        using var svc = new S3SyncService
        {
            Endpoint = "https://s3.amazonaws.com",
            Bucket = "my-bucket",
            AccessKey = "AKIAIOSFODNN7EXAMPLE",
            SecretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            Region = "us-east-1"
        };

        svc.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_MissingEndpoint_ReturnsFalse()
    {
        using var svc = new S3SyncService
        {
            Bucket = "my-bucket",
            AccessKey = "key",
            SecretKey = "secret"
        };
        svc.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_MissingBucket_ReturnsFalse()
    {
        using var svc = new S3SyncService
        {
            Endpoint = "https://s3.amazonaws.com",
            AccessKey = "key",
            SecretKey = "secret"
        };
        svc.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_MissingAccessKey_ReturnsFalse()
    {
        using var svc = new S3SyncService
        {
            Endpoint = "https://s3.amazonaws.com",
            Bucket = "bucket",
            SecretKey = "secret"
        };
        svc.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_MissingSecretKey_ReturnsFalse()
    {
        using var svc = new S3SyncService
        {
            Endpoint = "https://s3.amazonaws.com",
            Bucket = "bucket",
            AccessKey = "key"
        };
        svc.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_AllEmpty_ReturnsFalse()
    {
        using var svc = new S3SyncService();
        svc.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task PushAsync_NotConfigured_Throws()
    {
        using var svc = new S3SyncService();
        var act = async () => await svc.PushAsync("vault.okv");
        await act.Should().ThrowAsync<InvalidOperationException>("push without configuration should throw");
    }

    [Fact]
    public async Task PushAsync_ConfiguredButFileMissing_Throws()
    {
        using var svc = new S3SyncService
        {
            Endpoint = "https://s3.example.com",
            Bucket = "bucket",
            AccessKey = "key",
            SecretKey = "secret"
        };

        var act = async () => await svc.PushAsync(Path.Combine(_dir.Root, "nonexistent.okv"));
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task PullAsync_NotConfigured_Throws()
    {
        using var svc = new S3SyncService();
        var act = async () => await svc.PullAsync("vault.okv");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        using var svc = new S3SyncService();
        svc.Region.Should().Be("us-east-1");
        svc.RemoteFilePath.Should().Be("vault.okv");
    }

    [Fact]
    public void RemoteFilePath_CanBeChanged()
    {
        using var svc = new S3SyncService
        {
            Endpoint = "https://s3.example.com",
            Bucket = "bucket",
            AccessKey = "key",
            SecretKey = "secret",
            RemoteFilePath = "backups/vault-v2.okv"
        };
        svc.RemoteFilePath.Should().Be("backups/vault-v2.okv");
    }
}
