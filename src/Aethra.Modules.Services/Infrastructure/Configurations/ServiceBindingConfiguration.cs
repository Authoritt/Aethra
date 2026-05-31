using Aethra.Modules.Services.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Services.Infrastructure.Configurations;

internal sealed class ServiceBindingConfiguration : IEntityTypeConfiguration<ServiceBinding>
{
    public void Configure(EntityTypeBuilder<ServiceBinding> builder)
    {
        builder.ToTable("service_bindings");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<ServiceBindingId, string>(
                id => id.ToString(),
                s => ParseServiceBindingId(s)))
            .HasMaxLength(64);

        builder.Property(b => b.ServiceId)
            .HasColumnName("service_id")
            .HasConversion(new ValueConverter<ManagedServiceId, string>(
                id => id.ToString(),
                s => ParseManagedServiceId(s)))
            .HasMaxLength(64)
            .IsRequired();

        // FK explicito a managed_services.id sin cascade-delete: si borran el ManagedService
        // queremos bloquear hasta que los bindings se revoquen/eliminen manualmente para evitar
        // que apps queden referenciando un service inexistente sin trazabilidad.
        builder.HasOne<ManagedService>()
            .WithMany()
            .HasForeignKey(b => b.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.InstanceId).HasColumnName("instance_id").HasMaxLength(64).IsRequired();
        builder.Property(b => b.ResourceName).HasColumnName("resource_name").HasMaxLength(255).IsRequired();

        builder.Property(b => b.CredentialsCipher)
            .HasColumnName("credentials_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(b => b.Permissions)
            .HasColumnName("permissions")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(b => b.InjectedEnvVarPrefix)
            .HasColumnName("injected_env_var_prefix")
            .HasMaxLength(32)
            .IsRequired();

        builder.OwnsOne(b => b.MigrationsHook, hb =>
        {
            hb.Property(h => h.Command).HasColumnName("migrations_hook_command").HasMaxLength(512);
            hb.Property(h => h.TimeoutSeconds).HasColumnName("migrations_hook_timeout_seconds");
            hb.Property(h => h.FailDeployOnError).HasColumnName("migrations_hook_fail_on_error");
            hb.Property(h => h.RunOn)
                .HasColumnName("migrations_hook_run_on")
                .HasConversion<string>()
                .HasMaxLength(32);
        });

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.ProvisionedAt).HasColumnName("provisioned_at");
        builder.Property(b => b.RevokedAt).HasColumnName("revoked_at");
        builder.Property(b => b.LastRotatedAt).HasColumnName("last_rotated_at");

        // Unicidad parcial: solo un binding activo por (instance, service). Bindings revocados
        // se preservan para auditoria/rotacion historica, asi que filtramos por revoked_at IS NULL.
        builder.HasIndex(b => new { b.InstanceId, b.ServiceId })
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ux_service_bindings_instance_service_active");

        builder.Ignore(b => b.DomainEvents);
    }

    private static ServiceBindingId ParseServiceBindingId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ServiceBindingId(parsed.Value) : default;

    private static ManagedServiceId ParseManagedServiceId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ManagedServiceId(parsed.Value) : default;
}
