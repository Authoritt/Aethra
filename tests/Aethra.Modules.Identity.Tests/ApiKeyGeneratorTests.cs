using Aethra.Modules.Identity.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// <see cref="ApiKeyGenerator"/> emite los secrets de API key (<c>aethra_</c> + 32 chars Base32 sin
/// símbolos ambiguos). El secret es random, así que probamos propiedades del contrato (prefijo,
/// largo total, alfabeto, unicidad) y las ramas de <see cref="ApiKeyGenerator.ExtractVisiblePrefix"/>.
/// </summary>
public sealed class ApiKeyGeneratorTests
{
    // Espejo del alfabeto del generador (sin 0/1/I/L/O), 31 chars.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    [Fact]
    public void Generate_has_prefix_total_length_and_alphabet_only_body()
    {
        var secret = ApiKeyGenerator.Generate();

        secret.Should().StartWith(ApiKeyGenerator.SecretPrefix);
        secret.Length.Should().Be(ApiKeyGenerator.TotalLength);

        var body = secret[ApiKeyGenerator.SecretPrefix.Length..];
        body.Length.Should().Be(ApiKeyGenerator.Base32Length);
        body.All(c => Alphabet.Contains(c)).Should().BeTrue();
    }

    [Fact]
    public void Generate_produces_unique_secrets()
    {
        var secrets = Enumerable.Range(0, 50)
            .Select(_ => ApiKeyGenerator.Generate())
            .ToHashSet(StringComparer.Ordinal);

        secrets.Should().HaveCount(50);
    }

    [Fact]
    public void ExtractVisiblePrefix_returns_first_chars_after_the_known_prefix()
    {
        var secret = ApiKeyGenerator.Generate();
        var body = secret[ApiKeyGenerator.SecretPrefix.Length..];

        ApiKeyGenerator.ExtractVisiblePrefix(secret)
            .Should().Be(body[..ApiKeyGenerator.VisiblePrefixLength]);
    }

    [Fact]
    public void ExtractVisiblePrefix_without_known_prefix_returns_leading_chars()
    {
        ApiKeyGenerator.ExtractVisiblePrefix("XYZ123456789").Should().Be("XYZ12345");
    }

    [Fact]
    public void ExtractVisiblePrefix_returns_whole_string_when_shorter_than_visible_length()
    {
        ApiKeyGenerator.ExtractVisiblePrefix("abc").Should().Be("abc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractVisiblePrefix_throws_on_null_or_empty(string? secret)
    {
        var act = () => ApiKeyGenerator.ExtractVisiblePrefix(secret!);

        act.Should().Throw<ArgumentException>();
    }
}
