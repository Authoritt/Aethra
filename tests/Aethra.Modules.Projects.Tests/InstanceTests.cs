using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Instances.Events;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Instance"/>: composición de <c>Slug</c>/<c>ContainerName</c>,
/// guards de <c>Create</c>, normalización de <c>TrackedRef</c> y — lo más crítico — la cascada de
/// <see cref="Instance.ResolveTrackedRef"/> (explícito → mapping del template → DefaultBranch).
/// </summary>
public sealed class InstanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeWebhookSecretCodec : IWebhookSecretCodec
    {
        public byte[] Encode(string plainSecret) => System.Text.Encoding.UTF8.GetBytes(plainSecret);
        public string Decode(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);
    }

    private static Instance BuildInstance(string environment, string? trackedRef = null)
        => Instance.Create(TemplateId.New(), ClientId.New(), environment, "vm_1", "acme", "northwind",
            null, null, null, autoDeployOnNewBuild: false, Now, trackedRef: trackedRef);

    private static Template BuildTemplate(string defaultBranch, params (string Env, string Branch)[] mappings)
    {
        var slug = Slug.Create("app").Value;
        var url = GitRepoUrl.Create("https://github.com/acme/app").Value;
        var source = TemplateSource.Create(url, defaultBranch);
        var build = TemplateBuild.Nixpacks();
        var template = Template.Create(ProjectId.New(), slug, "App", source, build,
            "whsecret", new FakeWebhookSecretCodec(), Now);
        if (mappings.Length > 0)
        {
            template.ReplaceEnvironmentMapping(
                mappings.Select(m => new TemplateEnvironmentMapping(m.Env, m.Branch)).ToList(), Now);
        }
        return template;
    }

    // ---------- Create: composition ----------

    [Fact]
    public void Create_composes_lowercased_slug_and_container_name()
    {
        var inst = Instance.Create(TemplateId.New(), ClientId.New(), "Production", "vm_1", "Acme", "Northwind",
            null, null, null, autoDeployOnNewBuild: false, Now);

        inst.Environment.Should().Be("production");
        inst.Slug.Should().Be("northwind-production");
        inst.ContainerName.Should().Be("acme-northwind-production");
    }

    [Fact]
    public void Create_uses_slug_override_lowercased_and_trimmed()
    {
        var inst = Instance.Create(TemplateId.New(), ClientId.New(), "production", "vm_1", "acme", "northwind",
            null, null, null, autoDeployOnNewBuild: false, Now, slugOverride: "  Custom-Slug ");

        inst.Slug.Should().Be("custom-slug");
    }

    [Fact]
    public void Create_nullifies_blank_tracked_ref()
    {
        var inst = BuildInstance("production", trackedRef: "   ");

        inst.TrackedRef.Should().BeNull();
    }

    [Fact]
    public void Create_trims_tracked_ref()
    {
        var inst = BuildInstance("production", trackedRef: "  refs/heads/dev  ");

        inst.TrackedRef.Should().Be("refs/heads/dev");
    }

    [Fact]
    public void Create_raises_instance_created_event()
    {
        var inst = BuildInstance("production");

        inst.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InstanceCreatedEvent>();
    }

    [Theory]
    [InlineData("", "vm", "tmpl", "client")]
    [InlineData("prod", "", "tmpl", "client")]
    [InlineData("prod", "vm", "", "client")]
    [InlineData("prod", "vm", "tmpl", "")]
    public void Create_throws_when_a_required_field_is_blank(string env, string vm, string tslug, string cslug)
    {
        var act = () => Instance.Create(TemplateId.New(), ClientId.New(), env, vm, tslug, cslug,
            null, null, null, autoDeployOnNewBuild: false, Now);

        act.Should().Throw<ArgumentException>();
    }

    // ---------- SetTrackedRef ----------

    [Fact]
    public void SetTrackedRef_normalizes_blank_to_null()
    {
        var inst = BuildInstance("production", trackedRef: "refs/heads/x");

        inst.SetTrackedRef("   ", Now.AddMinutes(1));

        inst.TrackedRef.Should().BeNull();
    }

    [Fact]
    public void SetTrackedRef_trims_the_value()
    {
        var inst = BuildInstance("production");

        inst.SetTrackedRef("  refs/heads/y ", Now.AddMinutes(1));

        inst.TrackedRef.Should().Be("refs/heads/y");
    }

    [Fact]
    public void SetTrackedRef_is_a_noop_when_value_is_unchanged()
    {
        var inst = BuildInstance("production", trackedRef: "refs/heads/z");
        var before = inst.UpdatedAt;

        inst.SetTrackedRef("refs/heads/z", Now.AddHours(1));

        inst.TrackedRef.Should().Be("refs/heads/z");
        inst.UpdatedAt.Should().Be(before, "un set idempotente no debe marcar el aggregate como modificado");
    }

    // ---------- ResolveTrackedRef: the cascade ----------

    [Fact]
    public void ResolveTrackedRef_prefers_the_explicit_tracked_ref()
    {
        var template = BuildTemplate("main", ("production", "v2"));
        var inst = BuildInstance("production", trackedRef: "refs/pull/42/head");

        inst.ResolveTrackedRef(template).Should().Be("refs/pull/42/head");
    }

    [Fact]
    public void ResolveTrackedRef_falls_back_to_the_environment_mapping()
    {
        var template = BuildTemplate("main", ("production", "v2"));
        var inst = BuildInstance("production");

        inst.ResolveTrackedRef(template).Should().Be("refs/heads/v2");
    }

    [Fact]
    public void ResolveTrackedRef_falls_back_to_default_branch_when_no_mapping_matches()
    {
        var template = BuildTemplate("main", ("staging", "develop"));
        var inst = BuildInstance("production");

        inst.ResolveTrackedRef(template).Should().Be("refs/heads/main");
    }

    [Fact]
    public void ResolveTrackedRef_throws_on_null_template()
    {
        var inst = BuildInstance("production");

        var act = () => inst.ResolveTrackedRef(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
