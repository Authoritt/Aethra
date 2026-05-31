using Aethra.Modules.Deployments.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Deployments.Infrastructure.Configurations;

internal sealed class DeployJobConfiguration : IEntityTypeConfiguration<DeployJob>
{
    public void Configure(EntityTypeBuilder<DeployJob> builder)
    {
        builder.ToTable("deploy_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasColumnName("id")
            .HasConversion(new ValueConverter<DeployJobId, string>(
                id => id.ToString(),
                s => ParseDeployJobId(s)))
            .HasMaxLength(64);

        builder.Property(j => j.ApplicationId).HasColumnName("application_id").HasMaxLength(64).IsRequired();
        builder.Property(j => j.GitSha).HasColumnName("git_sha").HasMaxLength(64).IsRequired();
        builder.Property(j => j.Branch).HasColumnName("branch").HasMaxLength(255).IsRequired();
        builder.Property(j => j.Trigger).HasColumnName("trigger").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(j => j.TriggeredBy).HasColumnName("triggered_by").HasMaxLength(255);
        builder.Property(j => j.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(j => j.StartedAt).HasColumnName("started_at");
        builder.Property(j => j.FinishedAt).HasColumnName("finished_at");
        builder.Property(j => j.ImageTag).HasColumnName("image_tag").HasMaxLength(512);
        builder.Property(j => j.ContainerName).HasColumnName("container_name").HasMaxLength(255);
        builder.Property(j => j.ContainerPort).HasColumnName("container_port");
        builder.Property(j => j.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        builder.Property(j => j.ErrorMessage).HasColumnName("error_message").HasMaxLength(4000);
        builder.Property(j => j.FailedAtStage)
            .HasColumnName("failed_at_stage")
            .HasConversion<string?>()
            .HasMaxLength(16);

        // Idempotencia: un solo job activo por (application, git_sha). Útil para evitar
        // duplicados cuando un webhook se reenvía. La unicidad se aplica solo a estados
        // no-terminales — para historiar deploys del mismo SHA usaríamos un índice filtrado,
        // pero EF Core no soporta partial indexes portables. Lo dejamos como check en el handler.
        builder.HasIndex(j => new { j.ApplicationId, j.GitSha })
            .HasDatabaseName("ix_deploy_jobs_app_sha");

        builder.HasIndex(j => new { j.ApplicationId, j.CreatedAt })
            .HasDatabaseName("ix_deploy_jobs_app_time")
            .IsDescending(false, true);

        builder.HasMany(j => j.Logs)
            .WithOne()
            .HasForeignKey(l => l.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(DeployJob.Logs))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(j => j.DomainEvents);
    }

    private static DeployJobId ParseDeployJobId(string s)
        => AethraId.TryParse(s, out var p) ? new DeployJobId(p.Value) : default;
}
