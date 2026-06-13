using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Shared.Kernel.Tests;

/// <summary>
/// El <see cref="Slug"/> es el identificador URL-friendly usado por templates, instancias y
/// servicios: kebab-case, sin diacríticos, 1..64 chars. <c>Create</c> valida estricto;
/// <c>Suggest</c> normaliza texto libre a un slug válido.
/// </summary>
public sealed class SlugTests
{
    [Theory]
    [InlineData("mi-app", "mi-app")]
    [InlineData("backend", "backend")]
    [InlineData("proyecto-personal-2", "proyecto-personal-2")]
    [InlineData("a", "a")]
    [InlineData("123", "123")]
    [InlineData("  Backend  ", "backend")] // trim + lowercase
    [InlineData("MyApp", "myapp")]         // lowercase
    public void Create_accepts_and_normalizes_valid_slugs(string input, string expected)
    {
        var result = Slug.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "slug.empty")]
    [InlineData("   ", "slug.empty")]
    [InlineData("-leading", "slug.format")]
    [InlineData("trailing-", "slug.format")]
    [InlineData("double--dash", "slug.format")]
    [InlineData("under_score", "slug.format")]
    [InlineData("with space", "slug.format")]
    [InlineData("café", "slug.format")] // Create no quita diacríticos (eso es Suggest)
    public void Create_rejects_invalid_slugs_with_the_right_error_code(string input, string expectedCode)
    {
        var result = Slug.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Create_accepts_a_slug_of_exactly_64_chars()
    {
        var result = Slug.Create(new string('a', 64));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_a_slug_longer_than_64_chars()
    {
        var result = Slug.Create(new string('a', 65));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("slug.length");
    }

    [Theory]
    [InlineData("Café Münchën", "cafe-munchen")]
    [InlineData("My Project 2", "my-project-2")]
    [InlineData("a__b", "a-b")]
    [InlineData("--hello--", "hello")]
    [InlineData("Ñoño", "nono")]
    public void Suggest_normalizes_free_text_to_a_valid_slug(string input, string expected)
    {
        Slug.Suggest(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")] // todo inválido → vacío → fallback
    public void Suggest_falls_back_to_app_when_nothing_usable_remains(string input)
    {
        Slug.Suggest(input).Value.Should().Be("app");
    }

    [Fact]
    public void Suggest_truncates_to_64_chars()
    {
        Slug.Suggest(new string('a', 100)).Value.Length.Should().Be(64);
    }

    [Fact]
    public void Suggest_always_produces_a_value_that_Create_accepts()
    {
        var suggested = Slug.Suggest("  Algún Nombre Raro!!  con   espacios  ");

        Slug.Create(suggested.Value).IsSuccess.Should().BeTrue();
    }
}
