using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Aethra.ArchitectureTests;

/// <summary>
/// El namespace .Domain de cada modulo no debe depender de Entity Framework, ASP.NET,
/// ni cualquier infraestructura. Solo BCL + Aethra.Shared.Kernel.
/// </summary>
public sealed class DomainPurityTests
{
    private static readonly string[] ProhibidasEnDomain =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection",
        "Npgsql",
        "Docker.DotNet",
        "MediatR",
    ];

    [Fact]
    public void Domain_no_depende_de_infraestructura()
    {
        var moduleAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("Aethra.Modules.", StringComparison.Ordinal) ?? false)
            .ToList();

        // Forzar carga si AppDomain aun no las tiene.
        if (moduleAssemblies.Count == 0)
        {
            moduleAssemblies =
            [
                Assembly.Load("Aethra.Modules.Projects"),
                Assembly.Load("Aethra.Modules.Deployments"),
                Assembly.Load("Aethra.Modules.Services"),
                Assembly.Load("Aethra.Modules.Proxy"),
                Assembly.Load("Aethra.Modules.Vms"),
                Assembly.Load("Aethra.Modules.Metrics"),
                Assembly.Load("Aethra.Modules.Monitoring"),
                Assembly.Load("Aethra.Modules.Cloudflare"),
                Assembly.Load("Aethra.Modules.Notes"),
                Assembly.Load("Aethra.Modules.Identity"),
            ];
        }

        foreach (var assembly in moduleAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceEndingWith(".Domain")
                .Should()
                .NotHaveDependencyOnAny(ProhibidasEnDomain)
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Domain de '{assembly.GetName().Name}' tiene dependencia(s) a infraestructura: "
                + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }
}
