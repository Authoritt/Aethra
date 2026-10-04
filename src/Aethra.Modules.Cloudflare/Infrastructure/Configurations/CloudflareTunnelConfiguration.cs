using Aethra.Modules.Cloudflare.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Cloudflare.Infrastructure.Configurations;

internal sealed class CloudflareTunnelConfiguration : IEntityTypeConfiguration<CloudflareTunnel>
{
    public void Configure(EntityTypeBuilder<CloudflareTunnel> builder)
    {
        builder.ToTable("tunnels");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<CloudflareTunnelId, string>(
                id => id.ToString(),
                s => ParseId(s)))
            .HasMaxLength(64);

        builder.Property(t => t.TunnelId)
            .HasColumnName("external_tunnel_id")
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(t => t.TunnelId).IsUnique().HasDatabaseName("ux_tunnels_external_tunnel_id");

        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(t => t.AccountId).HasColumnName("account_id").HasMaxLength(64).IsRequired();

        builder.Property(t => t.ApiTokenCipher)
            .HasColumnName("api_token_cipher")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(t => t.AethraService).HasColumnName("aethra_service").HasMaxLength(256).IsRequired();
        builder.Property(t => t.FallbackService).HasColumnName("fallback_service").HasMaxLength(256).IsRequired();
        builder.Property(t => t.FallbackNoTlsVerify).HasColumnName("fallback_no_tls_verify").IsRequired();
        builder.Property(t => t.Mode)
            .HasColumnName("mode")
            .HasConversion(
                v => ToStorage(v),
                v => FromStorage(v))
            .HasMaxLength(32)
            .HasDefaultValue(TunnelMode.CentralProxy)
            .IsRequired();
        builder.Property(t => t.TargetVmId).HasColumnName("target_vm_id").HasMaxLength(64);
        builder.HasIndex(t => t.TargetVmId)
            .IsUnique()
            .HasFilter("mode = 'direct'")
            .HasDatabaseName("ux_tunnels_direct_target_vm_id");

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(t => t.LastSyncedAt).HasColumnName("last_synced_at");

        builder.Ignore(t => t.DomainEvents);
    }

    private static CloudflareTunnelId ParseId(string s)
        => AethraId.TryParse(s, out var parsed) ? new CloudflareTunnelId(parsed.Value) : default;

    private static string ToStorage(TunnelMode mode)
        => mode switch
        {
            TunnelMode.CentralProxy => "central_proxy",
            TunnelMode.Direct => "direct",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo de tunel no soportado."),
        };

    private static TunnelMode FromStorage(string mode)
        => mode switch
        {
            "central_proxy" => TunnelMode.CentralProxy,
            "direct" => TunnelMode.Direct,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo de tunel no soportado."),
        };
}
