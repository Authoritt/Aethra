using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Modules.Projects.Domain.Secrets;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Shared.Contracts.Projects;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>Cascada de env vars build-time para los --build-arg del deploy nativo.</summary>
public sealed class EfEnvironmentResolverBuildTimeTests(ProjectsDbFixture fixture)
    : IClassFixture<ProjectsDbFixture>
{
    private sealed class NoCodec : ISecretCodec
    {
        public byte[] Encode(string plainValue) => throw new InvalidOperationException();
        public string Decode(byte[] cipher) => throw new InvalidOperationException("los secretos no deben tocarse");
    }

    [Fact]
    public async Task Solo_IsBuildTime_con_cascada_instancia_gana_y_sin_secretos()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        db.EnvironmentVariables.AddRange(
            EnvironmentVariable.Create(EnvScopeType.Project, "prj_1", "NEXT_PUBLIC_URL", "http://proj", now, isBuildTime: true, isRuntime: true),
            EnvironmentVariable.Create(EnvScopeType.Instance, "ins_1", "NEXT_PUBLIC_URL", "https://prod", now, isBuildTime: true, isRuntime: true),
            EnvironmentVariable.Create(EnvScopeType.Instance, "ins_1", "SOLO_RUNTIME", "x", now, isBuildTime: false, isRuntime: true),
            EnvironmentVariable.Create(EnvScopeType.Instance, "ins_2", "OTRA_INSTANCIA", "x", now, isBuildTime: true, isRuntime: true));
        await db.SaveChangesAsync();

        var r = await new EfEnvironmentResolver(db, new NoCodec()).ResolveBuildTimeEnvAsync(
            new EnvironmentScopeChain("prj_1", "tpl_1", "cli_1", "ins_1"), CancellationToken.None);

        r.Should().BeEquivalentTo(new Dictionary<string, string> { ["NEXT_PUBLIC_URL"] = "https://prod" });
    }
}
