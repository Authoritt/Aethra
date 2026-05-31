using Aethra.Modules.Services.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Services.Infrastructure.Configurations;

internal sealed class ManagedServiceConfiguration : IEntityTypeConfiguration<ManagedService>
{
    public void Configure(EntityTypeBuilder<ManagedService> builder)
    {
        builder.ToTable("managed_services");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<ManagedServiceId, string>(
                id => id.ToString(),
                s => ParseManagedServiceId(s)))
            .HasMaxLength(64);

        builder.Property(s => s.Slug)
            .HasColumnName("slug")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(s => s.Slug)
            .IsUnique()
            .HasDatabaseName("ux_managed_services_slug");

        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(255).IsRequired();

        builder.Property(s => s.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.Version).HasColumnName("version").HasMaxLength(32).IsRequired();
        builder.Property(s => s.TargetVmId).HasColumnName("target_vm_id").HasMaxLength(64).IsRequired();
        builder.Property(s => s.ContainerName).HasColumnName("container_name").HasMaxLength(255).IsRequired();
        builder.Property(s => s.Image).HasColumnName("image").HasMaxLength(512).IsRequired();
        builder.Property(s => s.InternalPort).HasColumnName("internal_port").IsRequired();
        builder.Property(s => s.NetworkName).HasColumnName("network_name").HasMaxLength(64).IsRequired();

        builder.Property(s => s.AdminCredentialsCipher)
            .HasColumnName("admin_credentials_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(s => s.ExposedExternally).HasColumnName("exposed_externally").IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(s => s.ProvisionedAt).HasColumnName("provisioned_at");
        builder.Property(s => s.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        builder.Property(s => s.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);

        builder.Ignore(s => s.DomainEvents);
    }

    private static ManagedServiceId ParseManagedServiceId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ManagedServiceId(parsed.Value) : default;
}
