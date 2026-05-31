using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Infrastructure.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aethra.Modules.Projects.Infrastructure.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("applications");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasConversion(ValueConverters.ApplicationIdConverter)
            .HasMaxLength(64);

        builder.Property(a => a.EnvironmentId)
            .HasColumnName("environment_id")
            .HasConversion(ValueConverters.EnvironmentIdConverter)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.Slug)
            .HasColumnName("slug")
            .HasConversion(ValueConverters.SlugConverter)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(a => new { a.EnvironmentId, a.Slug })
            .IsUnique()
            .HasDatabaseName("ux_applications_env_slug");

        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Owned: Source
        builder.OwnsOne(a => a.Source, src =>
        {
            src.Property(s => s.GitRepoUrl)
                .HasColumnName("source_git_repo_url")
                .HasConversion(ValueConverters.GitRepoUrlConverter)
                .HasMaxLength(512)
                .IsRequired();
            src.Property(s => s.Branch).HasColumnName("source_branch").HasMaxLength(128).IsRequired();
            src.Property(s => s.WebhookSecret).HasColumnName("source_webhook_secret").HasMaxLength(128).IsRequired();
            src.Property(s => s.BaseDirectory).HasColumnName("source_base_directory").HasMaxLength(512).IsRequired();
            src.Property(s => s.WatchPaths)
                .HasColumnName("source_watch_paths")
                .HasColumnType("text[]");
            src.Property(s => s.AccessTokenId).HasColumnName("source_access_token_id").HasMaxLength(128);
        });

        // Owned: Build
        builder.OwnsOne(a => a.Build, b =>
        {
            b.Property(x => x.Type).HasColumnName("build_type").HasConversion<string>().HasMaxLength(32).IsRequired();
            b.Property(x => x.Path).HasColumnName("build_path").HasMaxLength(512).IsRequired();
            b.OwnsMany(x => x.Args, args =>
            {
                args.ToTable("application_build_args");
                args.WithOwner().HasForeignKey("application_id");
                args.Property<int>("id").ValueGeneratedOnAdd();
                args.HasKey("id");
                args.Property(a => a.Key).HasColumnName("key").HasMaxLength(256).IsRequired();
                args.Property(a => a.Value).HasColumnName("value").HasMaxLength(4000).IsRequired();
            });
        });

        // Owned: Runtime
        builder.OwnsOne(a => a.Runtime, r =>
        {
            r.Property(x => x.TargetVmId).HasColumnName("runtime_target_vm_id").HasMaxLength(64).IsRequired();
            r.Property(x => x.ContainerName)
                .HasColumnName("runtime_container_name")
                .HasConversion(ValueConverters.ContainerNameConverter)
                .HasMaxLength(255)
                .IsRequired();

            r.OwnsMany(x => x.Ports, p =>
            {
                p.ToTable("application_runtime_ports");
                p.WithOwner().HasForeignKey("application_id");
                p.Property<int>("id").ValueGeneratedOnAdd();
                p.HasKey("id");
                p.Property(x => x.ContainerPort)
                    .HasColumnName("container_port")
                    .HasConversion(ValueConverters.PortConverter);
                p.Property(x => x.HostPort).HasColumnName("host_port");
                p.Property(x => x.Protocol).HasColumnName("protocol").HasMaxLength(8);
            });

            r.OwnsMany(x => x.Volumes, v =>
            {
                v.ToTable("application_runtime_volumes");
                v.WithOwner().HasForeignKey("application_id");
                v.Property<int>("id").ValueGeneratedOnAdd();
                v.HasKey("id");
                v.Property(x => x.HostPath).HasColumnName("host_path").HasMaxLength(1024).IsRequired();
                v.Property(x => x.ContainerPath).HasColumnName("container_path").HasMaxLength(1024).IsRequired();
                v.Property(x => x.ReadOnly).HasColumnName("read_only");
            });

            r.OwnsOne(x => x.Healthcheck, hc =>
            {
                hc.Property(x => x.Cmd).HasColumnName("hc_cmd").HasColumnType("text[]");
                hc.Property(x => x.Interval).HasColumnName("hc_interval");
                hc.Property(x => x.Timeout).HasColumnName("hc_timeout");
                hc.Property(x => x.Retries).HasColumnName("hc_retries");
                hc.Property(x => x.StartPeriod).HasColumnName("hc_start_period");
            });
        });

        builder.Ignore(a => a.DomainEvents);
    }
}
