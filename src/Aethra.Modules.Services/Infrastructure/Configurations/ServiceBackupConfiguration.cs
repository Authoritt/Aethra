using Aethra.Modules.Services.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Services.Infrastructure.Configurations;

internal sealed class ServiceBackupConfiguration : IEntityTypeConfiguration<ServiceBackup>
{
    public void Configure(EntityTypeBuilder<ServiceBackup> builder)
    {
        builder.ToTable("service_backups");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<ServiceBackupId, string>(
                id => id.ToString(),
                s => ParseId(s)))
            .HasMaxLength(64);

        builder.Property(b => b.ServiceId)
            .HasColumnName("service_id")
            .HasConversion(new ValueConverter<ManagedServiceId, string>(
                id => id.ToString(),
                s => ParseServiceId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(b => b.ServiceId)
            .HasDatabaseName("ix_service_backups_service");

        builder.Property(b => b.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(b => b.FinishedAt).HasColumnName("finished_at");

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(b => b.SizeBytes).HasColumnName("size_bytes");
        builder.Property(b => b.DestinationPath).HasColumnName("destination_path").HasMaxLength(500).IsRequired();
        builder.Property(b => b.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);

        builder.Ignore(b => b.DomainEvents);
    }

    private static ServiceBackupId ParseId(string s)
        => AethraId.TryParse(s, out var p) ? new ServiceBackupId(p.Value) : default;

    private static ManagedServiceId ParseServiceId(string s)
        => AethraId.TryParse(s, out var p) ? new ManagedServiceId(p.Value) : default;
}
