using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

/// <summary>
/// Mapeo EF Core del aggregate <see cref="Client"/>. Schema: <c>projects</c>.
///
/// Un Client pertenece a un Project y su <c>Slug</c> es único dentro de ese Project — el
/// índice compuesto <c>ux_clients_project_slug</c> protege ese invariante en BD.
/// </summary>
internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<ClientId, string>(
                id => id.ToString(),
                s => ParseClientId(s)))
            .HasMaxLength(64);

        builder.Property(c => c.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(new ValueConverter<ProjectId, string>(
                id => id.ToString(),
                s => ParseProjectId(s)))
            .HasMaxLength(64)
            .IsRequired();

        // FK explícito a projects.id. Restrict: no permitir borrar un Project con Clients vivos
        // (rompería bindings y atribución de costos).
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.ProjectId).HasDatabaseName("ix_clients_project_id");

        // El Slug aquí NO usa el VO Slug del kernel — el dominio impone su propio regex más
        // restrictivo (ver Client.NormalizeSlug). Se persiste como string plano.
        builder.Property(c => c.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();

        builder.HasIndex(c => new { c.ProjectId, c.Slug })
            .IsUnique()
            .HasDatabaseName("ux_clients_project_slug");

        builder.Property(c => c.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(c => c.ContactEmail).HasColumnName("contact_email").HasMaxLength(255);
        builder.Property(c => c.BillingTag).HasColumnName("billing_tag").HasMaxLength(128);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(c => c.DomainEvents);
    }

    private static ClientId ParseClientId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ClientId(parsed.Value) : default;

    private static ProjectId ParseProjectId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ProjectId(parsed.Value) : default;
}
