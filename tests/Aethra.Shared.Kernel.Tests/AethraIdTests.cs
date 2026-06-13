using Aethra.Shared.Kernel.Ids;
using FluentAssertions;
using Xunit;

namespace Aethra.Shared.Kernel.Tests;

/// <summary>
/// <see cref="AethraId"/> es el id con prefijo (estilo Stripe) que envuelve cada entidad: Guid v7
/// codificado en Base32 Crockford. Cubrimos el round-trip ToString↔TryParse, el formato, el
/// lowercasing del prefijo, el rechazo de entradas mal formadas y la tolerancia Crockford a
/// símbolos confundibles (O→0, I/L→1) al decodificar.
/// </summary>
public sealed class AethraIdTests
{
    [Theory]
    [InlineData("vm")]
    [InlineData("app")]
    [InlineData("cert")]
    public void ToString_then_TryParse_round_trips(string prefix)
    {
        var id = AethraId.NewId(prefix);

        AethraId.TryParse(id.ToString(), out var parsed).Should().BeTrue();
        var parsedId = parsed!.Value;
        parsedId.Prefix.Should().Be(prefix);
        parsedId.Value.Should().Be(id.Value);
    }

    [Fact]
    public void ToString_uses_prefix_underscore_and_26_char_encoding()
    {
        var text = AethraId.NewId("vm").ToString();

        text.Should().StartWith("vm_");
        text.Split('_')[1].Length.Should().Be(26);
    }

    [Fact]
    public void Ctor_lowercases_the_prefix()
    {
        var id = new AethraId("VM", Guid.CreateVersion7());

        id.Prefix.Should().Be("vm");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_throws_on_blank_prefix(string prefix)
    {
        var act = () => new AethraId(prefix, Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noseparator")]
    [InlineData("trailingunderscore_")]
    public void TryParse_rejects_malformed_input(string? input)
    {
        AethraId.TryParse(input, out var id).Should().BeFalse();
        id.Should().BeNull();
    }

    [Fact]
    public void TryParse_rejects_structurally_invalid_encodings()
    {
        AethraId.TryParse("_" + new string('0', 26), out _).Should().BeFalse();   // prefijo vacío
        AethraId.TryParse("vm_" + new string('0', 25), out _).Should().BeFalse(); // encoded corto
        AethraId.TryParse("vm_" + new string('0', 27), out _).Should().BeFalse(); // encoded largo
        AethraId.TryParse("vm_" + new string('U', 26), out _).Should().BeFalse(); // 'U' fuera del alfabeto
    }

    [Fact]
    public void TryParse_is_case_insensitive_for_the_encoded_part()
    {
        var id = AethraId.NewId("vm");

        AethraId.TryParse(id.ToString().ToLowerInvariant(), out var parsed).Should().BeTrue();
        parsed!.Value.Value.Should().Be(id.Value);
    }

    [Fact]
    public void TryParse_tolerates_crockford_confusable_O_as_zero()
    {
        AethraId.TryParse("vm_" + new string('0', 26), out var zeros).Should().BeTrue();
        AethraId.TryParse("vm_" + new string('O', 26), out var ohs).Should().BeTrue();

        zeros!.Value.Value.Should().Be(Guid.Empty);
        ohs!.Value.Value.Should().Be(zeros.Value.Value);
    }

    [Fact]
    public void TryParse_tolerates_crockford_confusables_I_and_L_as_one()
    {
        AethraId.TryParse("vm_" + new string('1', 26), out var ones).Should().BeTrue();
        AethraId.TryParse("vm_" + new string('I', 26), out var eyes).Should().BeTrue();
        AethraId.TryParse("vm_" + new string('L', 26), out var els).Should().BeTrue();

        eyes!.Value.Value.Should().Be(ones!.Value.Value);
        els!.Value.Value.Should().Be(ones.Value.Value);
    }
}
