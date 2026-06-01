using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Infrastructure.Build;
using Aethra.Modules.Deployments.Infrastructure.Deployment;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Deployments.Infrastructure;

/// <summary>
/// DbContext del módulo Deployments. Schema PostgreSQL: <c>deployments</c>.
/// Hereda outbox_messages de la base.
///
/// F9.3 (pipeline completo): registra los DbSets de
/// <list type="bullet">
///   <item><see cref="Domain.Build.Build"/> + <see cref="BuildLogEntry"/> — entregables del agente A7.</item>
///   <item><see cref="Domain.Deployment.Deployment"/> + <see cref="DeploymentLogEntry"/> — entregables del agente A8.</item>
/// </list>
///
/// Las dos áreas comparten schema y tabla outbox; el orquestador de Deployment publica
/// <c>DeploymentCompletedIntegrationEvent</c> via outbox para que el módulo Proxy actualice la
/// Route en el atomic swap. El handler MediatR cross-module <c>BuildCompletedHandler</c> cierra
/// el lazo: Build OK → fan-out a N Deployments por cada Instance con auto-deploy.
/// </summary>
public sealed class DeploymentsDbContext(DbContextOptions<DeploymentsDbContext> options)
    : AethraModuleDbContext(options)
{
    public override string SchemaName => "deployments";

    // A7 — Build
    public DbSet<Domain.Build.Build> Builds => Set<Domain.Build.Build>();
    public DbSet<BuildLogEntry> BuildLogs => Set<BuildLogEntry>();

    // A8 — Deployment
    public DbSet<Domain.Deployment.Deployment> Deployments => Set<Domain.Deployment.Deployment>();
    public DbSet<DeploymentLogEntry> DeploymentLogs => Set<DeploymentLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new BuildConfiguration());
        modelBuilder.ApplyConfiguration(new BuildLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new DeploymentConfiguration());
        modelBuilder.ApplyConfiguration(new DeploymentLogEntryConfiguration());
    }
}
