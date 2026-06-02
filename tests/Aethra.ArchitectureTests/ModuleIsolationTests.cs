using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Aethra.ArchitectureTests;

/// <summary>
/// Verifica que cada modulo respeta la regla de oro: un modulo NO referencia internals de otro.
/// Solo puede comunicarse cross-module via:
///   - Aethra.Shared.Kernel (primitivos)
///   - Aethra.Shared.Contracts (eventos de integracion)
///   - Aethra.Shared.Infrastructure (pipelines/outbox base)
/// </summary>
public sealed class ModuleIsolationTests
{
    private static readonly string[] ModuleNames =
    [
        "Projects", "Deployments", "Services", "Proxy", "Vms",
        "Metrics", "Monitoring", "Cloudflare", "Notes", "Identity", "Notifications"
    ];

    [Fact]
    public void Ningun_modulo_referencia_internals_de_otro_modulo()
    {
        foreach (var moduleName in ModuleNames)
        {
            var assembly = LoadModuleAssembly(moduleName);

            var otrosModulos = ModuleNames
                .Where(m => m != moduleName)
                .Select(m => $"Aethra.Modules.{m}")
                .ToArray();

            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(otrosModulos)
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Modulo '{moduleName}' tiene dependencia(s) prohibida(s) a otro modulo: "
                + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    private static Assembly LoadModuleAssembly(string moduleName)
    {
        var assemblyName = $"Aethra.Modules.{moduleName}";
        return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName)
            ?? Assembly.Load(assemblyName);
    }
}
