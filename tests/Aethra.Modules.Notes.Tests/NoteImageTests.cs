using Aethra.Modules.Notes.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Notes.Tests;

/// <summary>
/// Invariantes de <see cref="NoteImage"/> (owned entity): exige filenames + content-type no vacíos
/// y un tamaño positivo; recorta espacios.
/// </summary>
public sealed class NoteImageTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_and_preserves_valid_metadata()
    {
        var id = Guid.NewGuid();

        var image = NoteImage.Create(id, "  photo.png ", " stored/abc.png ", " image/png ", 2048, Now);

        image.Id.Should().Be(id);
        image.OriginalFilename.Should().Be("photo.png");
        image.StoredFilename.Should().Be("stored/abc.png");
        image.ContentType.Should().Be("image/png");
        image.SizeBytes.Should().Be(2048);
        image.UploadedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("", "stored.png", "image/png")]
    [InlineData("   ", "stored.png", "image/png")]
    [InlineData("photo.png", "", "image/png")]
    [InlineData("photo.png", "stored.png", "")]
    public void Create_throws_when_a_required_string_is_blank(string original, string stored, string contentType)
    {
        var act = () => NoteImage.Create(Guid.NewGuid(), original, stored, contentType, 100, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_throws_on_non_positive_size(int sizeBytes)
    {
        var act = () => NoteImage.Create(Guid.NewGuid(), "p.png", "s.png", "image/png", sizeBytes, Now);

        act.Should().Throw<ArgumentException>();
    }
}
