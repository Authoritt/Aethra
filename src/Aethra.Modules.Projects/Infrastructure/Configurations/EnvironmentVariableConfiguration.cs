using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Modules.Projects.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

internal sealed class EnvironmentVariableConfiguration : IEntityTypeConfiguration<EnvironmentVariable>
{
    public void Configure(EntityTypeBuilder<EnvironmentVariable> builder)
    {
        builder.ToTable("env_vars");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.EnvVarIdConverter)
            .HasMaxLength(64);

        builder.Property(v => v.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(v => v.ScopeId).HasColumnName("scope_id").HasMaxLength(64).IsRequired();
        builder.Property(v => v.Key).HasColumnName("key").HasMaxLength(256).IsRequired();
        builder.Property(v => v.Value).HasColumnName("value").IsRequired();
        builder.Property(v => v.IsBuildTime).HasColumnName("is_build_time");
        builder.Property(v => v.IsRuntime).HasColumnName("is_runtime");
        builder.Property(v => v.IsSecret).HasColumnName("is_secret");
        builder.Property(v => v.IsLiteral).HasColumnName("is_literal");
        builder.Property(v => v.IsMultiline).HasColumnName("is_multiline");
        builder.Property(v => v.Source).HasColumnName("source").HasMaxLength(128);
        builder.Property(v => v.CreatedAt).HasColumnName("created_at");
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at");

        // Una variable por (scope, key). El resolver maneja la sobreescritura entre scopes.
        builder.HasIndex(v => new { v.ScopeType, v.ScopeId, v.Key })
            .IsUnique()
            .HasDatabaseName("ux_env_vars_scope_key");

        // Acelera revoke selectivo por source (binding:bnd_*).
        builder.HasIndex(v => new { v.ScopeType, v.ScopeId, v.Source })
            .HasDatabaseName("ix_env_vars_scope_source");
    }
}
