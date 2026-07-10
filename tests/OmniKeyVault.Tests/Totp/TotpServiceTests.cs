using System.Text;
using FluentAssertions;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using Xunit;

namespace OmniKeyVault.Tests.Totp;

/// <summary>
/// Tests for the TOTP service (RFC 6238). Validates the standard test vectors
/// from RFC 6238 Appendix B (SHA-1, 8-digit codes), the standard 6-digit
/// configuration, otpauth:// URI parsing, base32 encoding, and the
/// remaining-seconds countdown used by the UI per PRD §5.8 / UI_UX_SPEC §4.4.2.
/// </summary>
public class TotpServiceTests
{
    private readonly TotpService _svc = new();

    // RFC 6238 Appendix B test vectors (8-digit codes, SHA-1).
    // Secret: ASCII "12345678901234567890" = 0x3132...39.
    public static IEnumerable<object[]> Rfc6238TestVectors() => new List<object[]>
    {
        new object[] { 59UL,          "94287082" },
        new object[] { 1111111109UL,  "07081804" },
        new object[] { 1111111111UL,  "14050471" },
        new object[] { 1234567890UL,  "89005924" },
        new object[] { 2000000000UL,  "69279037" },
        new object[] { 20000000000UL, "65353130" }
    };

    [Theory]
    [MemberData(nameof(Rfc6238TestVectors))]
    public void GenerateCode_Rfc6238_TestVectors_MatchExactly(ulong unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var at = DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds);
        var code = _svc.GenerateCode(secret, at, digits: 8, periodSeconds: 30);
        code.Should().Be(expected, $"RFC 6238 Appendix B vector for T={unixSeconds}");
    }

    [Fact]
    public void GenerateCode_DefaultDigits_IsSix()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var code = _svc.GenerateCode(secret);
        code.Length.Should().Be(6);
        code.Should().MatchRegex("^[0-9]{6}$");
    }

    [Fact]
    public void GenerateCode_SameWindow_ReturnsSameCode()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        // Both timestamps fall in the same 30-second window [30, 60) -> counter=1.
        var t1 = DateTimeOffset.FromUnixTimeSeconds(30);
        var t2 = DateTimeOffset.FromUnixTimeSeconds(59);
        _svc.GenerateCode(secret, t1).Should().Be(_svc.GenerateCode(secret, t2));
    }

    [Fact]
    public void GenerateCode_DifferentWindows_ReturnsDifferentCodes()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        // t=29 -> counter=0, t=30 -> counter=1 (window boundary).
        var t1 = DateTimeOffset.FromUnixTimeSeconds(29);
        var t2 = DateTimeOffset.FromUnixTimeSeconds(30);
        _svc.GenerateCode(secret, t1).Should().NotBe(_svc.GenerateCode(secret, t2));
    }

    [Fact]
    public void GenerateCode_EmptySecret_Throws()
    {
        var act = () => _svc.GenerateCode(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<ValidationException>();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(11)]
    public void GenerateCode_DigitsOutOfRange_Throws(int digits)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var act = () => _svc.GenerateCode(secret, digits: digits);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void GenerateCode_PeriodZero_Throws()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var act = () => _svc.GenerateCode(secret, periodSeconds: 0);
        act.Should().Throw<ValidationException>();
    }

    // ---- Remaining seconds ----

    [Fact]
    public void GetRemainingSeconds_AtStartOfWindow_ReturnsPeriod()
    {
        // At t=30 we're at the start of window [30,60) — 30s remaining.
        var at = DateTimeOffset.FromUnixTimeSeconds(30);
        _svc.GetRemainingSeconds(at).Should().Be(30);
    }

    [Fact]
    public void GetRemainingSeconds_MidwayThroughWindow_ReturnsHalf()
    {
        // At t=45, we're 15s into window [30,60) (15s left).
        var at = DateTimeOffset.FromUnixTimeSeconds(45);
        _svc.GetRemainingSeconds(at).Should().Be(15);
    }

    [Fact]
    public void GetRemainingSeconds_AtEndOfWindow_ReturnsOneSecond()
    {
        // At t=59 we're 29s into window [30,60) (1s left).
        var at = DateTimeOffset.FromUnixTimeSeconds(59);
        _svc.GetRemainingSeconds(at).Should().Be(1);
    }

    [Fact]
    public void GetRemainingSeconds_ExactWindowBoundary_ReturnsPeriod()
    {
        // At t=60, we're at the start of window [60,90) (30s left).
        var at = DateTimeOffset.FromUnixTimeSeconds(60);
        _svc.GetRemainingSeconds(at).Should().Be(30);
    }

    [Fact]
    public void GetRemainingSeconds_CustomPeriod60()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(40);  // 40s into a 60s window
        _svc.GetRemainingSeconds(at, periodSeconds: 60).Should().Be(20);
    }

    // ---- otpauth:// URI parsing ----

    [Fact]
    public void ParseSecretFromUri_Rfc6238Sample_Succeeds()
    {
        // Standard Google Authenticator example URI.
        var uri = "otpauth://totp/ACME%20Co:john.doe@secret.example?secret=JBSWY3DPEHPK3PXP&issuer=ACME%20Co&period=30&digits=6";
        var secret = _svc.ParseSecretFromUri(uri);
        // "JBSWY3DPEHPK3PXP" (16 base32 chars) decodes to 10 bytes: "Hello!" + 0xDE 0xAD 0xBE 0xEF.
        secret.Length.Should().Be(10);
        Encoding.ASCII.GetString(secret.Take(6).ToArray()).Should().Be("Hello!");
    }

    [Fact]
    public void ParseSecretFromUri_WithoutQuery_Throws()
    {
        var act = () => _svc.ParseSecretFromUri("otpauth://totp/ACME");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void ParseSecretFromUri_Hotp_Throws()
    {
        // Only TOTP is supported.
        var act = () => _svc.ParseSecretFromUri("otpauth://hotp/ACME?secret=JBSWY3DPEHPK3PXP");
        act.Should().Throw<ValidationException>().WithMessage("*TOTP*");
    }

    [Fact]
    public void ParseSecretFromUri_MissingSecret_Throws()
    {
        var act = () => _svc.ParseSecretFromUri("otpauth://totp/ACME?issuer=ACME");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void ParseSecretFromUri_EmptyString_Throws()
    {
        var act = () => _svc.ParseSecretFromUri("");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void ParseSecretFromUri_NotOtpauth_Throws()
    {
        var act = () => _svc.ParseSecretFromUri("https://example.com/secret=JBSWY3DPEHPK3PXP");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void BuildUri_RoundTripsThroughParse()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var uri = _svc.BuildUri("ACME:user@example.com", secret, issuer: "ACME", digits: 6, periodSeconds: 30);
        uri.Should().StartWith("otpauth://totp/");
        var parsed = _svc.ParseSecretFromUri(uri);
        parsed.Should().Equal(secret);
    }

    // ---- Base32 ----

    [Fact]
    public void Base32_EmptyInput_ReturnsEmpty()
    {
        _svc.Base32Encode(Array.Empty<byte>()).Should().Be("");
        _svc.Base32Decode("").Should().BeEmpty();
    }

    [Fact]
    public void Base32_KnownVector_Encodes_NoPadding()
    {
        // Per the documented behavior: our Base32Encode omits padding (the
        // decoder accepts both padded and unpadded). Compare against unpadded RFC 4648 §10 vectors.
        _svc.Base32Encode(Encoding.ASCII.GetBytes("f")).Should().Be("MY");
        _svc.Base32Encode(Encoding.ASCII.GetBytes("fo")).Should().Be("MZXQ");
        _svc.Base32Encode(Encoding.ASCII.GetBytes("foo")).Should().Be("MZXW6");
        _svc.Base32Encode(Encoding.ASCII.GetBytes("foob")).Should().Be("MZXW6YQ");
        _svc.Base32Encode(Encoding.ASCII.GetBytes("fooba")).Should().Be("MZXW6YTB");
        _svc.Base32Encode(Encoding.ASCII.GetBytes("foobar")).Should().Be("MZXW6YTBOI");
    }

    [Fact]
    public void Base32_Decode_KnownVector_AcceptsPaddedAndUnpadded()
    {
        // Decoder should accept both padded and unpadded forms.
        Encoding.ASCII.GetString(_svc.Base32Decode("MZXW6YTBOI")).Should().Be("foobar");
        Encoding.ASCII.GetString(_svc.Base32Decode("MZXW6YTBOI======")).Should().Be("foobar");
        Encoding.ASCII.GetString(_svc.Base32Decode("JBSWY3DPEHPK3PXP")).Should().StartWith("Hello!");
    }

    [Fact]
    public void Base32_Roundtrip_VariousLengths()
    {
        for (int n = 0; n <= 32; n++)
        {
            var data = new byte[n];
            for (int i = 0; i < n; i++) data[i] = (byte)(i * 7 + 13);
            var encoded = _svc.Base32Encode(data);
            var decoded = _svc.Base32Decode(encoded);
            decoded.Should().Equal(data, $"roundtrip failed for length {n}");
        }
    }

    [Fact]
    public void Base32_Decode_AcceptsLowercase()
    {
        var upper = _svc.Base32Encode(Encoding.ASCII.GetBytes("Hello World!"));
        var lower = upper.ToLowerInvariant();
        var decoded = _svc.Base32Decode(lower);
        Encoding.ASCII.GetString(decoded).Should().Be("Hello World!");
    }

    [Fact]
    public void Base32_Decode_InvalidChar_Throws()
    {
        // '0' is not a valid base32 char.
        var act = () => _svc.Base32Decode("JBSWY3D0");
        act.Should().Throw<ValidationException>();
    }
}
