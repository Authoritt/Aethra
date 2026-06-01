using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

/// <summary>
/// Mapeo EF Core de la entidad <see cref="EnvironmentVariable"/>. Schema: <c>projects</c>.
///
/// Tabla polimórfica: (<see cref="EnvironmentVariable.ScopeType"/>, <see cref="EnvironmentVariable.ScopeId"/>)
/// identifica el aggregate dueño. No hay FK formal porque <c>ScopeId</c> es heterogéneo
/// (<c>prj_*</c>, <c>tpl_*</c>, <c>cli_*</c>, <c>ins_*</c>) — el invariante se mantiene en el
/// writer.
///
/// El índice único <c>ux_env_vars_scope_key</c> garantiza que no haya duplicados en un mismo
/// scope; <c>ix_env_vars_scope_source</c> acelera el revoke selectivo por <c>Source</c>.
/// </summary>
internal sealed class EnvironmentVariableConfiguration : IEntityTypeConfiguration<EnvironmentVariable>
{
    public void Configure(EntityTypeBuilder<EnvironmentVariable> builder)
    {
        builder.ToTable("env_vars");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<EnvVarId, string>(
                id => id.ToString(),
                s => ParseEnvVarId(s)))
            .HasMaxLength(64);

        builder.Property(e => e.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.ScopeId).HasColumnName("scope_id").HasMaxLength(64).IsRequired();
        builder.Property(e => e.Key).HasColumnName("key").HasMaxLength(256).IsRequired();
        builder.Property(e => e.Value).HasColumnName("value").HasColumnType("text").IsRequired();
        builder.Property(e => e.IsBuildTime).HasColumnName("is_build_time").IsRequired();
        builder.Property(e => e.IsRuntime).HasColumnName("is_runtime").IsRequired();
        builder.Property(e => e.IsLiteral).HasColumnName("is_literal").IsRequired();
        builder.Property(e => e.IsMultiline).HasColumnName("is_multiline").IsRequired();
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(128);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => new { e.ScopeType, e.ScopeId, e.Key })
            .IsUnique()
            .HasDatabaseName("ux_env_vars_scope_key");

        builder.HasIndex(e => new { e.ScopeType, e.ScopeId, e.Source })
            .HasDatabaseName("ix_env_vars_scope_source");
    }

    private static EnvVarId ParseEnvVarId(string s)
        => AethraId.TryParse(s, out var parsed) ? new EnvVarId(parsed.Value) : default;
}
