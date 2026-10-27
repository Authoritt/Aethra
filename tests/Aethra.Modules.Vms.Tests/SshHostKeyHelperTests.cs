using System.Security.Cryptography;
using Aethra.Modules.Vms.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Vms.Tests;

public sealed class SshHostKeyHelperTests
{
    private static readonly byte[] SampleKeyBytes = [
        0x00, 0x00, 0x00, 0x0b, 0x73, 0x73, 0x68, 0x2d, 0x65, 0x64, 0x32, 0x35, 0x35, 0x31, 0x39,
        0x00, 0x00, 0x00, 0x20, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b,
        0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a,
        0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    ];

    [Fact]
    public void FormatFingerprint_produces_canonical_openssh_sha256_format()
    {
        var fp = SshHostKeyHelper.FormatFingerprint(SampleKeyBytes);

        fp.Should().StartWith("SHA256:");
        fp.Should().NotEndWith("=");
        // SHA256 base64 sin padding tiene 43 caracteres; con prefijo SHA256: tiene 50
        fp.Length.Should().Be(50);

        var expectedHash = SHA256.HashData(SampleKeyBytes);
        var expectedB64 = Convert.ToBase64String(expectedHash).TrimEnd('=');
        fp.Should().Be($"SHA256:{expectedB64}");
    }

    [Theory]
    [InlineData("SHA256:uD34F12345678901234567890123456789012345678", "SHA256:uD34F12345678901234567890123456789012345678")]
    [InlineData("sha256:uD34F12345678901234567890123456789012345678", "SHA256:uD34F12345678901234567890123456789012345678")]
    [InlineData("uD34F12345678901234567890123456789012345678=", "SHA256:uD34F12345678901234567890123456789012345678")]
    [InlineData("  uD34F12345678901234567890123456789012345678   ", "SHA256:uD34F12345678901234567890123456789012345678")]
    [InlineData("SHA256:uD34F12345678901234567890123456789012345678==", "SHA256:uD34F12345678901234567890123456789012345678")]
    public void Normalize_standardizes_prefixes_and_padding(string input, string expected)
    {
        SshHostKeyHelper.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SHA256:")]
    [InlineData("sha256:   ")]
    public void Normalize_returns_null_for_empty_or_whitespace(string? input)
    {
        SshHostKeyHelper.Normalize(input).Should().BeNull();
    }

    [Fact]
    public void FingerprintsMatch_returns_true_for_equivalent_representations()
    {
        const string raw = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";
        const string withPrefix = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";
        const string withPrefixAndPad = "sha256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";

        SshHostKeyHelper.FingerprintsMatch(raw, withPrefix).Should().BeTrue();
        SshHostKeyHelper.FingerprintsMatch(withPrefix, withPrefixAndPad).Should().BeTrue();
        SshHostKeyHelper.FingerprintsMatch(raw, withPrefixAndPad).Should().BeTrue();
    }

    [Fact]
    public void FingerprintsMatch_returns_false_for_different_fingerprints()
    {
        const string fp1 = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";
        const string fp2 = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        SshHostKeyHelper.FingerprintsMatch(fp1, fp2).Should().BeFalse();
    }

    [Fact]
    public void IsValidFingerprint_validates_exact_256bit_sha256_hashes()
    {
        const string validFp = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";
        SshHostKeyHelper.IsValidFingerprint(validFp).Should().BeTrue();

        // 31 bytes en base64 en lugar de 32
        SshHostKeyHelper.IsValidFingerprint("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").Should().BeFalse();
        // Caracteres inválidos
        SshHostKeyHelper.IsValidFingerprint("SHA256:invalid-characters-not-base64!!!!!!!").Should().BeFalse();
        // Nulo o vacío
        SshHostKeyHelper.IsValidFingerprint(null).Should().BeFalse();
        SshHostKeyHelper.IsValidFingerprint("   ").Should().BeFalse();
    }
}
