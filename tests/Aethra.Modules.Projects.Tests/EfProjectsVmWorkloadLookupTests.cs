using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

public sealed class EfProjectsVmWorkloadLookupTests(ProjectsDbFixture fixture)
    : IClassFixture<ProjectsDbFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListOnVmAsync_returns_only_instances_on_the_specified_vm()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();

        var project = Project.Create(Slug.Create("platform").Value, "Platform", Now);
        var template = Template.Create(
            project.Id,
            Slug.Create("web").Value,
            "Web",
            TemplateSource.Create(GitRepoUrl.Create("https://github.com/acme/web").Value, "main"),
            TemplateBuild.Nixpacks(),
            "secret",
            new DummyCodec(),
            Now);
        var client1 = Client.Create(project.Id, "acme", "Acme", Now);
        var client2 = Client.Create(project.Id, "beta", "Beta", Now);

        var inst1 = Instance.Create(
            template.Id, client1.Id, "production", "vm_target_1",
            template.Slug.Value, client1.Slug, null, null, null, false, Now);

        var inst2 = Instance.Create(
            template.Id, client2.Id, "staging", "vm_target_1",
            template.Slug.Value, client2.Slug, null, null, null, false, Now);

        var inst3 = Instance.Create(
            template.Id, client1.Id, "dev", "vm_target_2",
            template.Slug.Value, client1.Slug, null, null, null, false, Now);

        db.Projects.Add(project);
        db.Templates.Add(template);
        db.Clients.AddRange(client1, client2);
        db.Instances.AddRange(inst1, inst2, inst3);
        await db.SaveChangesAsync();

        var lookup = new EfProjectsVmWorkloadLookup(db);

        var results = await lookup.ListOnVmAsync("vm_target_1", CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Kind == "instance");
        results.Select(r => r.Id).Should().BeEquivalentTo([inst1.Id.ToString(), inst2.Id.ToString()]);
        results.Select(r => r.Name).Should().BeEquivalentTo([inst1.ContainerName, inst2.ContainerName]);

        var otherResults = await lookup.ListOnVmAsync("vm_target_2", CancellationToken.None);
        otherResults.Should().ContainSingle();
        otherResults[0].Id.Should().Be(inst3.Id.ToString());

        var emptyResults = await lookup.ListOnVmAsync("vm_none", CancellationToken.None);
        emptyResults.Should().BeEmpty();

        var blankResults = await lookup.ListOnVmAsync("", CancellationToken.None);
        blankResults.Should().BeEmpty();
    }

    private sealed class DummyCodec : IWebhookSecretCodec
    {
        public byte[] Encode(string plainSecret) => [1];
        public string Decode(byte[] cipher) => "secret";
    }
}
