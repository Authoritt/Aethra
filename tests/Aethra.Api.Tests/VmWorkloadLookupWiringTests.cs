using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// #70 — la protección del borrado de VMs depende de que el host registre un
/// <see cref="IVmWorkloadLookup"/> por cada módulo dueño de cargas. Si un módulo dejara de
/// registrarlo, el handler no vería esas cargas y volvería a dejar huérfanos sin ningún error.
/// </summary>
public sealed class VmWorkloadLookupWiringTests(AethraApiFactory factory) : IClassFixture<AethraApiFactory>
{
    [Fact]
    public void Host_registers_workload_lookups_for_instances_managed_services_and_tunnels()
    {
        using var scope = factory.Services.CreateScope();

        var lookups = scope.ServiceProvider.GetServices<IVmWorkloadLookup>()
            .Select(l => l.GetType().Name)
            .ToList();

        lookups.Should().BeEquivalentTo(
            "EfProjectsVmWorkloadLookup",
            "EfServicesVmWorkloadLookup",
            "EfCloudflareVmWorkloadLookup");
    }

    [Fact]
    public void Host_builds_the_delete_vm_handler_with_its_required_dependencies()
    {
        using var scope = factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetService<IRequestHandler<DeleteVmCommand, Result>>();

        handler.Should().NotBeNull();
    }
}
