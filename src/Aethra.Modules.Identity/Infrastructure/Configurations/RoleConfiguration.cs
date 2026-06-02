using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Identity.Infrastructure.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.RoleIdConverter)
            .HasMaxLength(64);

        builder.Property(r => r.Slug)
            .HasColumnName("slug")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();

        // Postgres text[] nativo, mismo patrón que ApiKey.Scopes — facilita @>/<@
        // queries para encontrar roles por scope si alguna fase futura lo requiere.
        builder.Property(r => r.Scopes)
            .HasColumnName("scopes")
            .HasColumnType("text[]")
            .HasConversion(
                v => v.ToArray(),
                v => (IReadOnlySet<string>)new HashSet<string>(v ?? Array.Empty<string>(), StringComparer.Ordinal))
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<string>>(
                (a, b) => a == null && b == null || a != null && b != null && a.SetEquals(b),
                v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode(StringComparison.Ordinal))),
                v => (IReadOnlySet<string>)new HashSet<string>(v, StringComparer.Ordinal)));

        builder.Property(r => r.IsSystem).HasColumnName("is_system").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(r => r.Slug)
            .IsUnique()
            .HasDatabaseName("ux_roles_slug");

        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        // PK compuesta (user_id, role_id) — un user no puede tener el mismo rol duplicado.
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.Property(ur => ur.UserId)
            .HasColumnName("user_id")
            .HasConversion(ValueConverters.UserIdConverter)
            .HasMaxLength(64);

        builder.Property(ur => ur.RoleId)
            .HasColumnName("role_id")
            .HasConversion(ValueConverters.RoleIdConverter)
            .HasMaxLength(64);

        builder.Property(ur => ur.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        // FK explícita hacia roles con cascade — borrar un rol arrastra sus asignaciones.
        // El borrado de roles del sistema lo bloquea el use case, no la BD, para mantener
        // un mensaje de error semántico.
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ur => ur.RoleId)
            .HasDatabaseName("ix_user_roles_role_id");
    }
}
