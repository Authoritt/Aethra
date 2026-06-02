using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Secrets;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

/// <summary>
/// Mapeo EF Core de la entidad <see cref="Secret"/>. Schema: <c>projects</c>, tabla
/// <c>secrets</c> (separada de <c>env_vars</c> a propósito: reduce el blast-radius de un leak).
///
/// El valor se guarda cifrado en <c>value_cipher</c> (bytea). El índice único
/// <c>ux_secrets_scope_key</c> evita duplicados por scope; <c>ix_secrets_scope_source</c>
/// acelera el revoke selectivo por <c>Source</c> (igual patrón que env_vars).
/// </summary>
internal sealed class SecretConfiguration : IEntityTypeConfiguration<Secret>
{
    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.ToTable("secrets");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<SecretId, string>(
                id => id.ToString(),
                s => ParseSecretId(s)))
            .HasMaxLength(64);

        builder.Property(e => e.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.ScopeId).HasColumnName("scope_id").HasMaxLength(64).IsRequired();
        builder.Property(e => e.Key).HasColumnName("key").HasMaxLength(256).IsRequired();
        builder.Property(e => e.ValueCipher).HasColumnName("value_cipher").HasColumnType("bytea").IsRequired();
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(128);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => new { e.ScopeType, e.ScopeId, e.Key })
            .IsUnique()
            .HasDatabaseName("ux_secrets_scope_key");

        builder.HasIndex(e => new { e.ScopeType, e.ScopeId, e.Source })
            .HasDatabaseName("ix_secrets_scope_source");
    }

    private static SecretId ParseSecretId(string s)
        => AethraId.TryParse(s, out var parsed) ? new SecretId(parsed.Value) : default;
}
