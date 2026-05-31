using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Notes.Infrastructure.Configurations;

internal sealed class PinnedFactConfiguration : IEntityTypeConfiguration<PinnedFact>
{
    public void Configure(EntityTypeBuilder<PinnedFact> builder)
    {
        builder.ToTable("pinned_facts");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.PinnedFactIdConverter)
            .HasMaxLength(64);

        builder.Property(f => f.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(f => f.ScopeId).HasColumnName("scope_id").HasMaxLength(64).IsRequired();
        builder.Property(f => f.Key).HasColumnName("key").HasMaxLength(128).IsRequired();

        builder.Property(f => f.ValueCipher)
            .HasColumnName("value_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(f => f.IsSecret).HasColumnName("is_secret").IsRequired();
        builder.Property(f => f.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(f => new { f.ScopeType, f.ScopeId, f.Key })
            .IsUnique()
            .HasDatabaseName("ux_pinned_facts_scope_key");

        builder.Ignore(f => f.DomainEvents);
    }
}
