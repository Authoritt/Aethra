using Aethra.Modules.Vms.Domain;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Vms.Infrastructure.Configurations;

internal sealed class VmConfiguration : IEntityTypeConfiguration<Vm>
{
    public void Configure(EntityTypeBuilder<Vm> builder)
    {
        builder.ToTable("vms");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<VmId, string>(
                id => id.ToString(),
                s => ParseVmId(s)))
            .HasMaxLength(64);

        builder.Property(v => v.Slug)
            .HasColumnName("slug")
            .HasConversion(new ValueConverter<Slug, string>(s => s.Value, v => Slug.Create(v).Value))
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(v => v.Slug).IsUnique().HasDatabaseName("ux_vms_slug");

        builder.Property(v => v.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(v => v.PublicIp).HasColumnName("public_ip").HasMaxLength(45);
        builder.Property(v => v.PrivateIp).HasColumnName("private_ip").HasMaxLength(45);
        builder.Property(v => v.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(v => v.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(v => v.LastConnectedAt).HasColumnName("last_connected_at");
        builder.Property(v => v.LastDisconnectedAt).HasColumnName("last_disconnected_at");

        builder.Property(v => v.Hostname).HasColumnName("hostname").HasMaxLength(255);
        builder.Property(v => v.KernelVersion).HasColumnName("kernel_version").HasMaxLength(255);
        builder.Property(v => v.CpuModel).HasColumnName("cpu_model").HasMaxLength(255);
        builder.Property(v => v.CpuCores).HasColumnName("cpu_cores");
        builder.Property(v => v.TotalMemoryBytes).HasColumnName("total_memory_bytes");
        builder.Property(v => v.ContainerRuntime).HasColumnName("container_runtime").HasMaxLength(32);
        builder.Property(v => v.ContainerRuntimeVersion).HasColumnName("container_runtime_version").HasMaxLength(128);
        builder.Property(v => v.RootDiskTotalBytes).HasColumnName("root_disk_total_bytes");
        builder.Property(v => v.RootDiskAvailableBytes).HasColumnName("root_disk_available_bytes");
        builder.Property(v => v.RuntimeSocketAccessible).HasColumnName("runtime_socket_accessible");
        builder.Property(v => v.DataVolumePath).HasColumnName("data_volume_path").HasMaxLength(512);
        builder.Property(v => v.DataVolumeTotalBytes).HasColumnName("data_volume_total_bytes");
        builder.Property(v => v.DataVolumeAvailableBytes).HasColumnName("data_volume_available_bytes");

        // F11.4 — campos de instalación remota del satélite.
        builder.Property(v => v.SshCredentialsCipher).HasColumnName("ssh_credentials_cipher");
        builder.Property(v => v.InstallStatus)
            .HasColumnName("install_status")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(v => v.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(v => v.InstallLog)
            .HasColumnName("install_log")
            .HasColumnType("text")
            .IsRequired();

        // F12.3 — opt-in al pool de previews.
        builder.Property(v => v.AcceptsPreviews)
            .HasColumnName("accepts_previews")
            .IsRequired()
            .HasDefaultValue(true);

        builder.OwnsOne(v => v.Satellite, s =>
        {
            s.Property(x => x.Id)
                .HasColumnName("satellite_id")
                .HasConversion(new ValueConverter<SatelliteId, string>(
                    id => id.ToString(),
                    str => ParseSatelliteId(str)))
                .HasMaxLength(64)
                .IsRequired();
            s.Property(x => x.AgentVersion).HasColumnName("satellite_agent_version").HasMaxLength(64);
            s.Property(x => x.LastHandshakeAt).HasColumnName("satellite_last_handshake_at");

            s.OwnsOne(x => x.Token, t =>
            {
                t.Property(x => x.Hash).HasColumnName("satellite_token_hash").HasMaxLength(128).IsRequired();
                t.Property(x => x.RotatedAt).HasColumnName("satellite_token_rotated_at").IsRequired();
                // Índice por hash para que el SatelliteAuthenticator haga lookup O(log n).
                t.HasIndex(x => x.Hash).HasDatabaseName("ix_vms_satellite_token_hash");
            });
        });

        builder.Ignore(v => v.DomainEvents);
    }

    private static VmId ParseVmId(string s)
        => AethraId.TryParse(s, out var parsed) ? new VmId(parsed.Value) : default;

    private static SatelliteId ParseSatelliteId(string s)
        => AethraId.TryParse(s, out var parsed) ? new SatelliteId(parsed.Value) : default;
}
