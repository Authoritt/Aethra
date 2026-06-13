using System.Text;
using Aethra.Modules.Identity.Domain.Totp;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// <see cref="TotpGenerator"/> implementa RFC 6238 (HMAC-SHA1, 6 dígitos, 30s) para el 2FA.
/// Verificamos contra vectores conocidos de la RFC (no circular), la ventana de drift ±1 paso,
/// el rechazo de códigos fuera de ventana/longitud, y el Base32 RFC 4648.
/// </summary>
public sealed class TotpGeneratorTests
{
    // Secret de la RFC 6238 Appendix B (modo SHA1): ASCII "12345678901234567890" (20 bytes).
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]         // RFC: 94287082 → 6 dígitos
    [InlineData(1111111111L, "050471")] // RFC: 14050471 → 6 dígitos
    public void Generate_matches_rfc6238_known_vectors(long unixTime, string expected)
        => TotpGenerator.Generate(RfcSecret, DateTimeOffset.FromUnixTimeSeconds(unixTime)).Should().Be(expected);

    [Fact]
    public void ValidateCode_accepts_the_current_code()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var code = TotpGenerator.Generate(RfcSecret, now);

        TotpGenerator.ValidateCode(RfcSecret, code, now).Should().BeTrue();
    }

    [Fact]
    public void ValidateCode_tolerates_one_step_of_clock_drift()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var code = TotpGenerator.Generate(RfcSecret, now);

        TotpGenerator.ValidateCode(RfcSecret, code, now.AddSeconds(30)).Should().BeTrue();
    }

    [Fact]
    public void ValidateCode_rejects_a_code_outside_the_window()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var code = TotpGenerator.Generate(RfcSecret, now);

        TotpGenerator.ValidateCode(RfcSecret, code, now.AddMinutes(5)).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]   // != 6 dígitos
    [InlineData("1234567")]
    public void ValidateCode_rejects_wrong_length_or_empty(string code)
        => TotpGenerator.ValidateCode(RfcSecret, code, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000))
            .Should().BeFalse();

    [Theory]
    [InlineData("f", "MY")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void ToBase32_matches_rfc4648_vectors(string input, string expected)
        => TotpGenerator.ToBase32(Encoding.ASCII.GetBytes(input)).Should().Be(expected);

    [Fact]
    public void ToBase32_of_empty_is_empty()
        => TotpGenerator.ToBase32(Array.Empty<byte>()).Should().BeEmpty();

    [Fact]
    public void GenerateSecret_returns_the_requested_length()
        => TotpGenerator.GenerateSecret(20).Length.Should().Be(20);

    [Fact]
    public void BuildOtpAuthUri_includes_secret_issuer_and_algorithm_params()
    {
        var uri = TotpGenerator.BuildOtpAuthUri("Aethra", "admin@example.com", RfcSecret);

        uri.Should().StartWith("otpauth://totp/Aethra:");
        uri.Should().Contain("secret=");
        uri.Should().Contain("issuer=Aethra");
        uri.Should().Contain("algorithm=SHA1");
        uri.Should().Contain("digits=6");
    }
}
