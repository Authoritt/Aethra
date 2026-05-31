// IMPORTANTE (wiring pendiente — leer antes de hacer build):
// Cuando crees ProxyDbContext, agrega:
//   public DbSet<Certificate> Certificates => Set<Certificate>();
//   public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();
// y en OnModelCreating después del base.OnModelCreating(modelBuilder):
//   modelBuilder.ApplyConfiguration(new CertificateConfiguration());
//   modelBuilder.ApplyConfiguration(new AcmeAccountConfiguration());
// Schema del módulo: "proxy".

using Aethra.Modules.Proxy.Domain;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Proxy.Infrastructure.Configurations;

internal sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        builder.ToTable("certificates");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<CertificateId, string>(
                id => id.ToString(),
                s => ParseCertificateId(s)))
            .HasMaxLength(64);

        builder.Property(c => c.Hostname)
            .HasColumnName("hostname")
            .HasConversion(new ValueConverter<Hostname, string>(
                h => h.Value,
                v => Hostname.Create(v).Value))
            .HasMaxLength(253)
            .IsRequired();

        // Unique para evitar dos certs activos al mismo hostname. Si se requieren históricos
        // se cambiará a (hostname, status) parcial — F3 sólo guarda el vigente.
        builder.HasIndex(c => c.Hostname)
            .IsUnique()
            .HasDatabaseName("ux_certificates_hostname");

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.PfxCipherText)
            .HasColumnName("pfx_cipher_text")
            .HasColumnType("text");

        builder.Property(c => c.IssuedAt).HasColumnName("issued_at");
        builder.Property(c => c.NotBefore).HasColumnName("not_before");
        builder.Property(c => c.NotAfter).HasColumnName("not_after");
        builder.Property(c => c.RenewAfter).HasColumnName("renew_after");
        builder.Property(c => c.LastError).HasColumnName("last_error").HasColumnType("text");

        // Índice para el worker que escanea próximos a expirar.
        builder.HasIndex(c => c.RenewAfter)
            .HasDatabaseName("ix_certificates_renew_after");

        builder.Ignore(c => c.DomainEvents);
    }

    private static CertificateId ParseCertificateId(string s)
        => AethraId.TryParse(s, out var parsed) ? new CertificateId(parsed.Value) : default;
}
