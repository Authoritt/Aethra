// WIRING PENDIENTE — ProxyDbContext NO declara DbSet<AcmeAccount> todavía. Cuando Johan
// wire la tabla, agregar en ProxyDbContext.cs:
//   public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();
// y en OnModelCreating:
//   modelBuilder.ApplyConfiguration(new AcmeAccountConfiguration());
// Sin esto la migración no incluirá tls_account y LetsEncryptCertManager fallará al primer
// arranque (no podrá persistir la account key ACME).

using Aethra.Modules.Proxy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Proxy.Infrastructure.Configurations;

/// <summary>
/// La tabla <c>tls_account</c> guarda una sola fila (id = "default") con la account key
/// ACME cifrada. La unicidad se garantiza por el PK literal.
/// </summary>
internal sealed class AcmeAccountConfiguration : IEntityTypeConfiguration<AcmeAccount>
{
    public void Configure(EntityTypeBuilder<AcmeAccount> builder)
    {
        builder.ToTable("tls_account");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(a => a.AccountKeyPemCipherText)
            .HasColumnName("account_key_pem")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(a => a.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(a => a.UseStaging)
            .HasColumnName("use_staging")
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
