using Aethra.Modules.Identity.Domain.Totp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Identity.Infrastructure.Configurations;

internal sealed class TotpChallengeConfiguration : IEntityTypeConfiguration<TotpChallenge>
{
    public void Configure(EntityTypeBuilder<TotpChallenge> builder)
    {
        builder.ToTable("totp_challenges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(c => c.FailedAttempts).HasColumnName("failed_attempts").IsRequired();
        builder.Property(c => c.ConsumedAt).HasColumnName("consumed_at");

        builder.HasIndex(c => c.ExpiresAt).HasDatabaseName("ix_totp_challenges_expires_at");
        builder.HasIndex(c => c.UserId).HasDatabaseName("ix_totp_challenges_user_id");
    }
}
