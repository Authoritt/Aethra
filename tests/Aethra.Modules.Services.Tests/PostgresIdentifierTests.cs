using Aethra.Modules.Services.Infrastructure.Provisioning;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// <see cref="PostgresIdentifier.Quote"/> es la defensa contra inyección por identificadores
/// (Postgres no admite parámetros ligados para db/role/schema/table). Es un ALLOWLIST estricto
/// (<c>^[a-zA-Z_][a-zA-Z0-9_]{0,62}$</c>): RECHAZA cualquier carácter peligroso en vez de
/// escaparlo. Lo usan los provisioners y el dump multi-schema del PostgresBackupEngine, así que
/// un agujero acá = inyección SQL en operaciones admin. Sin cobertura previa.
/// </summary>
public sealed class PostgresIdentifierTests
{
    [Theory]
    [InlineData("public")]
    [InlineData("my_table")]
    [InlineData("_private")]
    [InlineData("T1")]
    [InlineData("a")]
    [InlineData("Schema_2024")]
    public void Quote_wraps_valid_identifier_in_double_quotes(string raw)
        => PostgresIdentifier.Quote(raw).Should().Be($"\"{raw}\"");

    [Fact]
    public void Quote_accepts_max_length_63()
    {
        var max = new string('a', 63); // 1 inicial + 62 → límite de identificador Postgres
        PostgresIdentifier.Quote(max).Should().Be($"\"{max}\"");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Quote_rejects_null_or_empty(string? raw)
    {
        var act = () => PostgresIdentifier.Quote(raw!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Quote_rejects_over_63_chars()
    {
        var tooLong = new string('a', 64);
        var act = () => PostgresIdentifier.Quote(tooLong);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("1abc")]      // empieza con dígito
    [InlineData("9")]         // dígito solo
    public void Quote_rejects_leading_digit(string raw)
    {
        var act = () => PostgresIdentifier.Quote(raw);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("a b")]                 // espacio
    [InlineData("a-b")]                 // guion
    [InlineData("a.b")]                 // punto (schema.table debe quotearse por separado)
    [InlineData("a\"b")]                // comilla doble (no se escapa, se rechaza)
    [InlineData("a'b")]                 // comilla simple
    [InlineData("a;b")]                 // punto y coma
    [InlineData("users; DROP TABLE x")] // vector de inyección clásico
    [InlineData("\" OR 1=1 --")]        // inyección por quoted identifier
    [InlineData("café")]                // no-ASCII
    [InlineData("a()")]                 // paréntesis
    public void Quote_rejects_dangerous_or_nonascii_characters(string raw)
    {
        var act = () => PostgresIdentifier.Quote(raw);
        act.Should().Throw<ArgumentException>();
    }
}
