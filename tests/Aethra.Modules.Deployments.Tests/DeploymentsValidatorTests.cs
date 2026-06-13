using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.UseCases.Build.Commands;
using Aethra.Modules.Deployments.UseCases.Deployment.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// Tests de los validators FluentValidation de los comandos de Build y Deployment: required ids
/// y el GitSha de TriggerBuild (mínimo 7 chars, formato short-sha). Puros, sin BD.
/// </summary>
public sealed class DeploymentsValidatorTests
{
    private static TriggerBuildCommand TB(string tpl = "tpl_1", string sha = "abc1234", string gitRef = "refs/heads/main")
        => new(tpl, sha, gitRef, BuildTrigger.Webhook, null);

    [Fact]
    public void TriggerBuild_accepts_a_valid_command()
        => new TriggerBuildValidator().Validate(TB()).IsValid.Should().BeTrue();

    [Fact]
    public void TriggerBuild_requires_template_and_ref()
    {
        new TriggerBuildValidator().Validate(TB(tpl: "")).IsValid.Should().BeFalse();
        new TriggerBuildValidator().Validate(TB(gitRef: "")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]        // vacío
    [InlineData("abc12")]   // 5 < 7
    public void TriggerBuild_rejects_short_or_empty_sha(string sha)
        => new TriggerBuildValidator().Validate(TB(sha: sha)).IsValid.Should().BeFalse();

    [Fact]
    public void CancelBuild_requires_build_id()
    {
        new CancelBuildValidator().Validate(new CancelBuildCommand("")).IsValid.Should().BeFalse();
        new CancelBuildValidator().Validate(new CancelBuildCommand("bld_1")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TriggerDeployment_requires_build_and_instance()
    {
        new TriggerDeploymentValidator().Validate(new TriggerDeploymentCommand("", "ins_1", null)).IsValid.Should().BeFalse();
        new TriggerDeploymentValidator().Validate(new TriggerDeploymentCommand("bld_1", "", null)).IsValid.Should().BeFalse();
        new TriggerDeploymentValidator().Validate(new TriggerDeploymentCommand("bld_1", "ins_1", null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PromoteDeployment_requires_source_and_target()
    {
        new PromoteDeploymentValidator().Validate(new PromoteDeploymentCommand("", "ins_2", null)).IsValid.Should().BeFalse();
        new PromoteDeploymentValidator().Validate(new PromoteDeploymentCommand("dep_1", "", null)).IsValid.Should().BeFalse();
        new PromoteDeploymentValidator().Validate(new PromoteDeploymentCommand("dep_1", "ins_2", null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void RollbackDeployment_requires_source()
    {
        new RollbackDeploymentValidator().Validate(new RollbackDeploymentCommand("", null)).IsValid.Should().BeFalse();
        new RollbackDeploymentValidator().Validate(new RollbackDeploymentCommand("dep_1", null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CancelDeployment_requires_deployment_id()
    {
        new CancelDeploymentValidator().Validate(new CancelDeploymentCommand("")).IsValid.Should().BeFalse();
        new CancelDeploymentValidator().Validate(new CancelDeploymentCommand("dep_1")).IsValid.Should().BeTrue();
    }
}
