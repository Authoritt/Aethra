using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Shared.Infrastructure.Persistence;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys", "shared");

        builder.HasKey(k => new { k.Key, k.RequestType });

        builder.Property(k => k.Key).HasMaxLength(128).IsRequired();
        builder.Property(k => k.RequestType).HasMaxLength(512).IsRequired();
        builder.Property(k => k.ResponseJson).IsRequired();
        builder.Property(k => k.CreatedAt).IsRequired();
        builder.Property(k => k.ExpiresAt).IsRequired();

        builder.HasIndex(k => k.ExpiresAt).HasDatabaseName("ix_idempotency_expires");
    }
}
