using Aethra.Modules.Identity.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Identity.Infrastructure.Configurations;

internal sealed class AuthenticationThrottleConfiguration : IEntityTypeConfiguration<AuthenticationThrottle>
{
    public void Configure(EntityTypeBuilder<AuthenticationThrottle> builder)
    {
        builder.ToTable("authentication_throttles");
        builder.HasKey(x => x.PartitionKey);

        builder.Property(x => x.PartitionKey)
            .HasColumnName("partition_key")
            .HasMaxLength(64);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.WindowStartedAt).HasColumnName("window_started_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_authentication_throttles_expires_at");
    }
}
