using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Identity.Infrastructure.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.UserIdConverter)
            .HasMaxLength(64);

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.PasswordHashCipher)
            .HasColumnName("password_hash_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100);

        // F12.3 — handle de GitHub para mapeo PR.user.login → User. Unique para evitar suplantación.
        builder.Property(u => u.GitHubUsername)
            .HasColumnName("github_username")
            .HasMaxLength(39);
        builder.HasIndex(u => u.GitHubUsername)
            .IsUnique()
            .HasDatabaseName("ux_users_github_username")
            .HasFilter("\"github_username\" IS NOT NULL");

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // F12.1B — 2FA TOTP.
        builder.Property(u => u.TotpSecretCipher)
            .HasColumnName("totp_secret_cipher")
            .HasColumnType("bytea");
        builder.Property(u => u.TotpEnabled)
            .HasColumnName("totp_enabled")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(u => u.TotpEnabledAt).HasColumnName("totp_enabled_at");
        builder.Property(u => u.TotpRecoveryCodesCipher)
            .HasColumnName("totp_recovery_codes_cipher")
            .HasColumnType("bytea");
        builder.Property(u => u.TotpRecoveryCodesUsedMask)
            .HasColumnName("totp_recovery_codes_used_mask")
            .HasDefaultValue(0)
            .IsRequired();

        // Email único — case-insensitive porque normalizamos a lowercase al persistir.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");

        builder.HasIndex(u => u.IsActive)
            .HasDatabaseName("ix_users_is_active");

        // Relación M:N con Role via UserRole (configurado por separado).
        builder.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(u => u.DomainEvents);
    }
}
