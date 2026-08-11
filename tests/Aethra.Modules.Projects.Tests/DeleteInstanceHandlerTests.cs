using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

public sealed class DeleteInstanceHandlerTests(ProjectsDbFixture fixture)
    : IClassFixture<ProjectsDbFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_rejects_non_ephemeral_without_confirmation_before_event_or_mutation()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var (_, _, instance) = SeedInstance(db, isEphemeral: false);
        var outbox = new RecordingOutbox();
        var handler = new DeleteInstanceHandler(db, new FixedClock(Now), outbox);

        var result = await handler.Handle(new DeleteInstanceCommand(instance.Id.ToString(), ForceEphemeral: false),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("instance.delete_requires_confirmation");
        result.Error.Message.Should().Contain("borrado destructivo");
        outbox.Events.Should().BeEmpty();
        (await db.Instances.AnyAsync(i => i.Id == instance.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_deletes_ephemeral_without_confirmation_and_enqueues_cleanup()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var (_, _, instance) = SeedInstance(db, isEphemeral: true);
        var outbox = new RecordingOutbox();
        var handler = new DeleteInstanceHandler(db, new FixedClock(Now), outbox);

        var result = await handler.Handle(new DeleteInstanceCommand(instance.Id.ToString(), ForceEphemeral: false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await db.Instances.AnyAsync(i => i.Id == instance.Id)).Should().BeFalse();
        var removed = outbox.Events.Should().ContainSingle().Which.Should()
            .BeOfType<InstanceRemovedIntegrationEvent>().Subject;
        removed.InstanceId.Should().Be(instance.Id.ToString());
        removed.RemovedAt.Should().Be(Now);
        removed.TargetVmId.Should().Be(instance.TargetVmId);
        removed.ContainerNames.Should().BeEquivalentTo(["web-acme-preview"]);
    }

    [Fact]
    public async Task Handle_deletes_non_ephemeral_with_confirmation_and_enqueues_cleanup()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var (_, _, instance) = SeedInstance(db, isEphemeral: false);
        var outbox = new RecordingOutbox();
        var handler = new DeleteInstanceHandler(db, new FixedClock(Now), outbox);

        var result = await handler.Handle(new DeleteInstanceCommand(instance.Id.ToString(), ForceEphemeral: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await db.Instances.AnyAsync(i => i.Id == instance.Id)).Should().BeFalse();
        outbox.Events.Should().ContainSingle()
            .Which.Should().BeOfType<InstanceRemovedIntegrationEvent>();
    }

    private static (Template Template, Client Client, Instance Instance) SeedInstance(
        ProjectsDbContext db,
        bool isEphemeral)
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
        var client = Client.Create(project.Id, "acme", "Acme", Now);
        var instance = Instance.Create(
            template.Id,
            client.Id,
            isEphemeral ? "preview" : "production",
            "vm_1",
            template.Slug.Value,
            client.Slug,
            null,
            null,
            null,
            autoDeployOnNewBuild: false,
            Now,
            isEphemeral: isEphemeral);

        db.Projects.Add(project);
        db.Templates.Add(template);
        db.Clients.Add(client);
        db.Instances.Add(instance);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return (template, client, instance);
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
