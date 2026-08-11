using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

public sealed class DeleteProjectHandlerTests(ProjectsDbFixture fixture)
    : IClassFixture<ProjectsDbFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_forced_delete_enqueues_instance_cleanup_before_removing_project_graph()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var (project, instance) = SeedProjectGraph(db);
        var outbox = new RecordingOutbox();
        var handler = new DeleteProjectHandler(db, new FixedClock(Now), outbox);

        var result = await handler.Handle(new DeleteProjectCommand(project.Id.ToString(), Force: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await db.Projects.AnyAsync(p => p.Id == project.Id)).Should().BeFalse();
        (await db.Instances.AnyAsync(i => i.Id == instance.Id)).Should().BeFalse();

        var removed = outbox.Events.Should().ContainSingle().Which.Should()
            .BeOfType<InstanceRemovedIntegrationEvent>().Subject;
        removed.InstanceId.Should().Be(instance.Id.ToString());
        removed.RemovedAt.Should().Be(Now);
        removed.TargetVmId.Should().Be("vm_1");
        removed.ContainerNames.Should().BeEquivalentTo([
            "web-acme-production",
            "acme-production-api",
            "acme-production-worker"
        ]);
    }

    [Fact]
    public async Task Handle_rejects_project_with_instances_without_force_before_cleanup_or_mutation()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var (project, instance) = SeedProjectGraph(db);
        var outbox = new RecordingOutbox();
        var handler = new DeleteProjectHandler(db, new FixedClock(Now), outbox);

        var result = await handler.Handle(new DeleteProjectCommand(project.Id.ToString(), Force: false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("project.has_instances");
        outbox.Events.Should().BeEmpty();
        (await db.Projects.AnyAsync(p => p.Id == project.Id)).Should().BeTrue();
        (await db.Instances.AnyAsync(i => i.Id == instance.Id)).Should().BeTrue();
    }

    private static (Aethra.Modules.Projects.Domain.Project Project, Instance Instance) SeedProjectGraph(
        ProjectsDbContext db)
    {
        var project = Aethra.Modules.Projects.Domain.Project.Create(
            Slug.Create("platform").Value, "Platform", Now);
        var template = Template.Create(
            project.Id,
            Slug.Create("web").Value,
            "Web",
            TemplateSource.Create(GitRepoUrl.Create("https://github.com/acme/web").Value, "main"),
            TemplateBuild.Nixpacks(),
            "secret",
            new PlainWebhookSecretCodec(),
            Now);
        template.ReplaceServices([
            new TemplateService("api", "web-api:latest", 8080, ["/"], []),
            new TemplateService("worker", "web-worker:latest", 9000, [], [])
        ], Now);
        var client = Client.Create(project.Id, "acme", "Acme", Now);
        var instance = Instance.Create(
            template.Id,
            client.Id,
            "production",
            "vm_1",
            template.Slug.Value,
            client.Slug,
            null,
            null,
            null,
            autoDeployOnNewBuild: false,
            Now);

        db.Projects.Add(project);
        db.Templates.Add(template);
        db.Clients.Add(client);
        db.Instances.Add(instance);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return (project, instance);
    }

    private sealed class RecordingOutbox : IOutboxWriter<ProjectsDbContext>
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public Task EnqueueAsync(IIntegrationEvent @event, CancellationToken ct)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class PlainWebhookSecretCodec : IWebhookSecretCodec
    {
        public byte[] Encode(string plainSecret) => System.Text.Encoding.UTF8.GetBytes(plainSecret);
        public string Decode(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);
    }
}
