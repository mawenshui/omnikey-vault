using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using Xunit;

namespace OmniKeyVault.Tests.V20;

/// <summary>
/// Tests for ShamirSecretSharing: split/combine roundtrip, threshold requirements,
/// edge cases, and hex encoding/decoding.
/// </summary>
public class ShamirSecretSharingTests
{
    [Fact]
    public void Split_Combine_Roundtrip_RestoresSecret()
    {
        var secret = Encoding.UTF8.GetBytes("my-master-password-123");
        var shares = ShamirSecretSharing.Split(secret, 5, 3);

        shares.Should().HaveCount(5);
        shares.Should().OnlyHaveUniqueItems(s => s.Index, "each share should have a unique index");

        // Reconstruct with any 3 shares
        var selected = shares.Take(3).ToArray();
        var reconstructed = ShamirSecretSharing.Combine(selected, secret.Length);

        reconstructed.Should().Equal(secret, "reconstructed secret must match original");
    }

    [Fact]
    public void Split_Combine_AnyKShares_RestoresSecret()
    {
        var secret = Encoding.UTF8.GetBytes("test-secret-data");
        var shares = ShamirSecretSharing.Split(secret, 5, 3);

        // Try different combinations of 3 shares
        var combos = new[]
        {
            new[] { shares[0], shares[1], shares[2] },
            new[] { shares[0], shares[2], shares[4] },
            new[] { shares[1], shares[3], shares[4] },
            new[] { shares[2], shares[3], shares[4] },
        };

        foreach (var combo in combos)
        {
            var reconstructed = ShamirSecretSharing.Combine(combo, secret.Length);
            reconstructed.Should().Equal(secret, $"combination [{string.Join(",", combo.Select(s => s.Index))}] should reconstruct the secret");
        }
    }

    [Fact]
    public void Split_Combine_MoreThanKShares_RestoresSecret()
    {
        var secret = Encoding.UTF8.GetBytes("extra-shares-test");
        var shares = ShamirSecretSharing.Split(secret, 6, 3);

        // Use 5 shares (more than k=3)
        var reconstructed = ShamirSecretSharing.Combine(shares.Take(5).ToArray(), secret.Length);
        reconstructed.Should().Equal(secret, "using more than k shares should still reconstruct correctly");
    }

    [Fact]
    public void Split_KGreaterThanOrEqualN_AllShares_ReconstructsSecret()
    {
        var secret = Encoding.UTF8.GetBytes("k-equals-n-test");
        var shares = ShamirSecretSharing.Split(secret, 3, 3);

        // Must use all 3 shares
        var reconstructed = ShamirSecretSharing.Combine(shares, secret.Length);
        reconstructed.Should().Equal(secret);
    }

    [Fact]
    public void Split_KLessThan2_Throws()
    {
        var secret = Encoding.UTF8.GetBytes("test");
        var act = () => ShamirSecretSharing.Split(secret, 5, 1);
        act.Should().Throw<ArgumentException>("k must be >= 2");
    }

    [Fact]
    public void Split_KGreaterThanN_Throws()
    {
        var secret = Encoding.UTF8.GetBytes("test");
        var act = () => ShamirSecretSharing.Split(secret, 3, 5);
        act.Should().Throw<ArgumentException>("k must be <= n");
    }

    [Fact]
    public void Split_NGreaterThan255_Throws()
    {
        var secret = Encoding.UTF8.GetBytes("test");
        var act = () => ShamirSecretSharing.Split(secret, 256, 2);
        act.Should().Throw<ArgumentException>("n must be <= 255");
    }

    [Fact]
    public void Split_EmptySecret_Throws()
    {
        var act = () => ShamirSecretSharing.Split(Array.Empty<byte>(), 3, 2);
        act.Should().Throw<ArgumentException>("secret is empty");
    }

    [Fact]
    public void Combine_LessThan2Shares_Throws()
    {
        var secret = Encoding.UTF8.GetBytes("test");
        var shares = ShamirSecretSharing.Split(secret, 3, 2);
        var act = () => ShamirSecretSharing.Combine(new[] { shares[0] }, secret.Length);
        act.Should().Throw<ArgumentException>("need at least 2 shares");
    }

    [Fact]
    public void Split_LargeSecret_Roundtrip()
    {
        var secret = Encoding.UTF8.GetBytes(new string('A', 256));
        var shares = ShamirSecretSharing.Split(secret, 4, 2);
        var reconstructed = ShamirSecretSharing.Combine(shares.Take(2).ToArray(), secret.Length);
        reconstructed.Should().Equal(secret);
    }

    [Fact]
    public void Split_SingleByteSecret_Roundtrip()
    {
        var secret = new byte[] { 0x42 };
        var shares = ShamirSecretSharing.Split(secret, 3, 2);
        var reconstructed = ShamirSecretSharing.Combine(shares.Take(2).ToArray(), 1);
        reconstructed.Should().Equal(secret);
    }

    [Fact]
    public void ShareToHex_HexToShare_Roundtrip()
    {
        var secret = Encoding.UTF8.GetBytes("hex-encoding-test");
        var shares = ShamirSecretSharing.Split(secret, 3, 2);

        foreach (var (index, share) in shares)
        {
            var hex = ShamirSecretSharing.ShareToHex(index, share);
            var (decodedIndex, decodedShare) = ShamirSecretSharing.HexToShare(hex);

            decodedIndex.Should().Be(index);
            decodedShare.Should().Equal(share);
        }
    }

    [Fact]
    public void Split_DifferentSecrets_ProduceDifferentShares()
    {
        var secret1 = Encoding.UTF8.GetBytes("secret-one");
        var secret2 = Encoding.UTF8.GetBytes("secret-two");

        var shares1 = ShamirSecretSharing.Split(secret1, 3, 2);
        var shares2 = ShamirSecretSharing.Split(secret2, 3, 2);

        // Shares should be different (extremely high probability)
        shares1[0].Share.Should().NotEqual(shares2[0].Share,
            "different secrets should produce different shares");
    }

    [Fact]
    public void Split_AllZeroSecret_Roundtrip()
    {
        var secret = new byte[16];
        var shares = ShamirSecretSharing.Split(secret, 3, 2);
        var reconstructed = ShamirSecretSharing.Combine(shares.Take(2).ToArray(), 16);
        reconstructed.Should().Equal(secret);
    }

    [Fact]
    public void Split_AllMaxByteSecret_Roundtrip()
    {
        var secret = Enumerable.Repeat((byte)0xFF, 16).ToArray();
        var shares = ShamirSecretSharing.Split(secret, 3, 2);
        var reconstructed = ShamirSecretSharing.Combine(shares.Take(2).ToArray(), 16);
        reconstructed.Should().Equal(secret);
    }
}
