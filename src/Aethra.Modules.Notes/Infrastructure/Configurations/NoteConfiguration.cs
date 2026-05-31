using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Notes.Infrastructure.Configurations;

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("notes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.NoteIdConverter)
            .HasMaxLength(64);

        builder.Property(n => n.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.ScopeId).HasColumnName("scope_id").HasMaxLength(64).IsRequired();
        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
        builder.Property(n => n.MarkdownBody).HasColumnName("markdown_body").IsRequired();
        builder.Property(n => n.IsPinned).HasColumnName("is_pinned").IsRequired();
        builder.Property(n => n.AuthorId).HasColumnName("author_id").HasMaxLength(64);
        builder.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(n => new { n.ScopeType, n.ScopeId })
            .HasDatabaseName("ix_notes_scope");

        builder.OwnsMany(n => n.Images, image =>
        {
            image.ToTable("note_images");

            // El owner es Note (Id: NoteId con conversion a string). EF requiere que la FK del
            // owned tipe el mismo tipo CLR que la PK del owner, así que tipamos shadow property como NoteId.
            image.WithOwner().HasForeignKey("NoteId");
            image.HasKey("NoteId", nameof(NoteImage.Id));

            image.Property<NoteId>("NoteId")
                .HasColumnName("note_id")
                .HasConversion(ValueConverters.NoteIdConverter)
                .HasMaxLength(64);

            image.Property(i => i.Id).HasColumnName("image_id");
            image.Property(i => i.OriginalFilename).HasColumnName("original_filename").HasMaxLength(255).IsRequired();
            image.Property(i => i.StoredFilename).HasColumnName("stored_filename").HasMaxLength(255).IsRequired();
            image.Property(i => i.ContentType).HasColumnName("content_type").HasMaxLength(64).IsRequired();
            image.Property(i => i.SizeBytes).HasColumnName("size_bytes").IsRequired();
            image.Property(i => i.UploadedAt).HasColumnName("uploaded_at").IsRequired();

            image.HasIndex(i => i.Id).HasDatabaseName("ix_note_images_image_id");
        });

        builder.Metadata.FindNavigation(nameof(Note.Images))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(n => n.DomainEvents);
    }
}
