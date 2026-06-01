using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Mapeo EF Core de <see cref="BuildLogEntry"/>. Tabla <c>build_logs</c>.
/// FK cascade contra <c>builds</c> — borrar un build borra sus logs.
/// </summary>
internal sealed class BuildLogEntryConfiguration : IEntityTypeConfiguration<BuildLogEntry>
{
    public void Configure(EntityTypeBuilder<BuildLogEntry> builder)
    {
        builder.ToTable("build_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<BuildLogId, string>(
                id => id.ToString(),
                s => ParseBuildLogId(s)))
            .HasMaxLength(64);

        builder.Property(l => l.BuildId)
            .HasColumnName("build_id")
            .HasConversion(new ValueConverter<BuildId, string>(
                id => id.ToString(),
                s => ParseBuildId(s)))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(l => l.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(l => l.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(l => l.Level)
            .HasColumnName("level")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();
        builder.Property(l => l.Stage)
            .HasColumnName("stage")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(l => l.Text)
            .HasColumnName("text")
            .IsRequired();

        // (build_id, sequence) único: orden estable de las líneas dentro de un build.
        builder.HasIndex(l => new { l.BuildId, l.Sequence })
            .HasDatabaseName("ix_build_logs_build_seq")
            .IsUnique();
    }

    private static BuildLogId ParseBuildLogId(string s)
        => AethraId.TryParse(s, out var parsed) ? new BuildLogId(parsed.Value) : default;

    private static BuildId ParseBuildId(string s)
        => AethraId.TryParse(s, out var parsed) ? new BuildId(parsed.Value) : default;
}
