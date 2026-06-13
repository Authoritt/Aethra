using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Domain.Events;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>
/// Invariantes del agregado <see cref="DnsRecord"/>: normalización (name lowercase, content/comment
/// trim, comment blank→null), validación de TTL [1,86400], y los eventos de ciclo de sync
/// (UpdateContent→Updated, MarkSynced→Created + limpia error, MarkRemoved→Deleted).
/// </summary>
public sealed class DnsRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CloudflareZoneId Zone => CloudflareZoneId.New();
    private static DnsRecord NewRecord()
        => DnsRecord.Create(Zone, DnsRecordType.A, "app.example.com", "1.2.3.4", 300, false, null, Now);

    [Fact]
    public void Create_lowercases_name_trims_content_and_starts_unsynced()
    {
        var record = DnsRecord.Create(Zone, DnsRecordType.A, "  App.Example.COM ", "  1.2.3.4 ", 300, true, "  note ", Now);

        record.Name.Should().Be("app.example.com");
        record.Content.Should().Be("1.2.3.4");
        record.Comment.Should().Be("note");
        record.Proxied.Should().BeTrue();
        record.ExternalRecordId.Should().BeNull();
        record.SyncedAt.Should().BeNull();
    }

    [Fact]
    public void Create_blank_comment_becomes_null()
    {
        DnsRecord.Create(Zone, DnsRecordType.A, "a.example.com", "1.2.3.4", 1, false, "   ", Now)
            .Comment.Should().BeNull();
    }

    [Theory]
    [InlineData("", "1.2.3.4")]
    [InlineData("   ", "1.2.3.4")]
    [InlineData("a.example.com", "")]
    [InlineData("a.example.com", "   ")]
    public void Create_throws_on_blank_name_or_content(string name, string content)
    {
        var act = () => DnsRecord.Create(Zone, DnsRecordType.A, name, content, 300, false, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86401)]
    public void Create_throws_on_ttl_out_of_range(int ttl)
    {
        var act = () => DnsRecord.Create(Zone, DnsRecordType.A, "a.example.com", "1.2.3.4", ttl, false, null, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(86400)]
    public void Create_accepts_ttl_at_the_bounds(int ttl)
    {
        DnsRecord.Create(Zone, DnsRecordType.A, "a.example.com", "1.2.3.4", ttl, false, null, Now)
            .Ttl.Should().Be(ttl);
    }

    [Fact]
    public void UpdateContent_updates_provided_fields_and_raises_event()
    {
        var record = NewRecord();

        record.UpdateContent("5.6.7.8", 600, true, "new note", Now.AddMinutes(1));

        record.Content.Should().Be("5.6.7.8");
        record.Ttl.Should().Be(600);
        record.Proxied.Should().BeTrue();
        record.Comment.Should().Be("new note");
        record.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<DnsRecordUpdatedEvent>();
    }

    [Fact]
    public void UpdateContent_throws_on_blank_content()
    {
        var record = NewRecord();

        var act = () => record.UpdateContent("   ", null, null, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateContent_blank_comment_clears_it()
    {
        var record = DnsRecord.Create(Zone, DnsRecordType.A, "a.example.com", "1.2.3.4", 300, false, "note", Now);

        record.UpdateContent(null, null, null, "   ", Now);

        record.Comment.Should().BeNull();
    }

    [Fact]
    public void MarkSynced_sets_external_id_clears_error_and_raises_created_event()
    {
        var record = NewRecord();
        record.MarkSyncFailed("prev error");

        record.MarkSynced("cf-rec-123", Now);

        record.ExternalRecordId.Should().Be("cf-rec-123");
        record.SyncedAt.Should().Be(Now);
        record.LastError.Should().BeNull();
        record.DomainEvents.OfType<DnsRecordCreatedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void MarkSyncFailed_records_the_error()
    {
        var record = NewRecord();

        record.MarkSyncFailed("boom");

        record.LastError.Should().Be("boom");
    }

    [Fact]
    public void MarkRemoved_raises_deleted_event()
    {
        var record = NewRecord();
        record.ClearDomainEvents();

        record.MarkRemoved();

        record.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<DnsRecordDeletedEvent>();
    }
}
