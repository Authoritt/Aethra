using System.Text;
using Aethra.Modules.Identity.Domain.Totp;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// <see cref="RecoveryCodes"/> respalda el 2FA: 10 códigos one-shot. Cubrimos generación/formato,
/// el round-trip Pack/Unpack legacy, el hashing one-way (Argon2id), verificación segura y el
/// bitmask de uso: un código usado NO puede reutilizarse.
/// </summary>
public sealed class RecoveryCodesTests
{
    [Fact]
    public void Generate_produces_the_default_count_of_well_formed_codes()
    {
        var codes = RecoveryCodes.Generate();

        codes.Should().HaveCount(RecoveryCodes.CodeCount);
        codes.Should().OnlyContain(c => c.Length == RecoveryCodes.CodeLength);
        codes.Should().OnlyContain(c => RecoveryCodes.LooksLikeRecoveryCode(c));
    }

    [Fact]
    public void Generate_honors_a_custom_count()
    {
        RecoveryCodes.Generate(3).Should().HaveCount(3);
    }

    [Fact]
    public void Pack_then_Unpack_round_trips_preserving_order()
    {
        var codes = new[] { "ABCD2345", "EFGH6789", "JKLM2345" };

        var unpacked = RecoveryCodes.Unpack(RecoveryCodes.Pack(codes));

        unpacked.Should().Equal(codes);
    }

    [Fact]
    public void Unpack_of_empty_payload_returns_empty()
    {
        RecoveryCodes.Unpack([]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("ABCD2345", true)]
    [InlineData("abcd2345", true)]    // case-insensitive
    [InlineData("12345678", false)]   // '1' no está en el alfabeto reducido
    [InlineData("ABCD234O", false)]   // 'O' excluido
    [InlineData("ABCD234", false)]    // muy corto
    [InlineData("ABCD23456", false)]  // muy largo
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeRecoveryCode_validates_the_format(string? input, bool expected)
    {
        RecoveryCodes.LooksLikeRecoveryCode(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_trims_and_uppercases()
    {
        RecoveryCodes.Normalize("  abcd2345  ").Should().Be("ABCD2345");
    }

    [Fact]
    public void TrySetUsed_marks_an_unused_index_and_rejects_reuse()
    {
        var mask = 0;

        RecoveryCodes.TrySetUsed(ref mask, 3).Should().BeTrue();
        RecoveryCodes.IsUsed(mask, 3).Should().BeTrue();
        RecoveryCodes.TrySetUsed(ref mask, 3).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)] // == CodeCount, fuera de rango
    public void TrySetUsed_rejects_out_of_range_index_without_mutating(int index)
    {
        var mask = 0;

        RecoveryCodes.TrySetUsed(ref mask, index).Should().BeFalse();
        mask.Should().Be(0);
    }

    [Fact]
    public void IsUsed_treats_out_of_range_as_used()
    {
        RecoveryCodes.IsUsed(0, -1).Should().BeTrue();
        RecoveryCodes.IsUsed(0, RecoveryCodes.CodeCount).Should().BeTrue();
    }

    [Fact]
    public void CountUsed_and_RemainingCount_track_the_mask()
    {
        var mask = 0;
        RecoveryCodes.RemainingCount(mask).Should().Be(RecoveryCodes.CodeCount);

        RecoveryCodes.TrySetUsed(ref mask, 0);
        RecoveryCodes.TrySetUsed(ref mask, 5);
        RecoveryCodes.TrySetUsed(ref mask, 9);

        RecoveryCodes.CountUsed(mask).Should().Be(3);
        RecoveryCodes.RemainingCount(mask).Should().Be(7);
    }

    [Fact]
    public void FormatRemainingFraction_renders_remaining_over_total()
    {
        var mask = 0;
        RecoveryCodes.TrySetUsed(ref mask, 0);
        RecoveryCodes.TrySetUsed(ref mask, 1);

        RecoveryCodes.FormatRemainingFraction(mask).Should().Be("8/10");
    }

    [Fact]
    public void Using_all_codes_leaves_zero_remaining()
    {
        var mask = 0;
        for (var i = 0; i < RecoveryCodes.CodeCount; i++)
        {
            RecoveryCodes.TrySetUsed(ref mask, i);
        }

        RecoveryCodes.RemainingCount(mask).Should().Be(0);
        RecoveryCodes.FormatRemainingFraction(mask).Should().Be("0/10");
    }

    // ---------- GH #91: One-way hashing tests ----------

    [Fact]
    public void Hash_produces_hashed_payload_with_header_and_cannot_be_unpacked_as_plain()
    {
        var codes = new[] { "ABCD2345", "EFGH6789" };
        var hashed = RecoveryCodes.Hash(codes);

        RecoveryCodes.IsHashed(hashed).Should().BeTrue();
        var unpackedPlain = RecoveryCodes.Unpack(hashed);
        // The first line is the header, not a code
        unpackedPlain[0].Should().Be("AETHRA-RCH-V1");

        // The hashes should be valid Argon2id hashes
        var hashes = RecoveryCodes.UnpackHashes(hashed);
        hashes.Should().HaveCount(2);
        hashes.Should().OnlyContain(h => h.StartsWith("$argon2id$v=19$m=65536,t=4,p=2$", StringComparison.Ordinal));
    }

    [Fact]
    public void Stored_hashes_do_not_contain_plaintext_codes()
    {
        var codes = new[] { "ABCD2345", "EFGH6789" };
        var hashed = RecoveryCodes.Hash(codes);
        var rawText = Encoding.UTF8.GetString(hashed);

        foreach (var code in codes)
        {
            rawText.Should().NotContain(code);
        }
    }

    [Fact]
    public void IsHashed_rejects_legacy_packed_payload()
    {
        var legacy = RecoveryCodes.Pack(["ABCD2345", "EFGH6789"]);
        RecoveryCodes.IsHashed(legacy).Should().BeFalse();
        RecoveryCodes.IsHashed(null).Should().BeFalse();
        RecoveryCodes.IsHashed([]).Should().BeFalse();
    }

    [Fact]
    public void FindMatchingCodeIndex_finds_correct_index_and_respects_used_mask()
    {
        var codes = new[] { "ABCD2345", "EFGH6789", "JKLM2345" };
        var hashed = RecoveryCodes.Hash(codes);
        var hashes = RecoveryCodes.UnpackHashes(hashed);

        // Matching code at index 1
        RecoveryCodes.FindMatchingCodeIndex("EFGH6789", hashes, usedMask: 0).Should().Be(1);

        // If index 1 is already used (bit 1 << 1 = 2)
        RecoveryCodes.FindMatchingCodeIndex("EFGH6789", hashes, usedMask: 2).Should().Be(-1);

        // Index 0 still works
        RecoveryCodes.FindMatchingCodeIndex("ABCD2345", hashes, usedMask: 2).Should().Be(0);

        // Wrong code returns -1
        RecoveryCodes.FindMatchingCodeIndex("WXYZ2345", hashes, usedMask: 0).Should().Be(-1);
    }

    [Fact]
    public void FindMatchingCodeIndex_normalizes_case_and_whitespace()
    {
        var codes = new[] { "ABCD2345" };
        var hashes = RecoveryCodes.UnpackHashes(RecoveryCodes.Hash(codes));

        RecoveryCodes.FindMatchingCodeIndex("  abcd2345  ", hashes, usedMask: 0).Should().Be(0);
    }

    [Fact]
    public void FindMatchingLegacyCodeIndex_finds_correct_index_and_respects_mask()
    {
        var codes = new[] { "ABCD2345", "EFGH6789" };

        RecoveryCodes.FindMatchingLegacyCodeIndex("efgh6789", codes, usedMask: 0).Should().Be(1);
        RecoveryCodes.FindMatchingLegacyCodeIndex("efgh6789", codes, usedMask: 2).Should().Be(-1);
        RecoveryCodes.FindMatchingLegacyCodeIndex("WXYZ2345", codes, usedMask: 0).Should().Be(-1);
    }
}
