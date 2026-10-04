using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>
/// OT-008 H-T8-4 - TunnelIngressSupport.LockTimeout es estatico y los tests lo acortan: toda clase que use
/// el lock de ingress va en esta coleccion, sin paralelizar, para que ningun otro test vea el timeout corto.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TunnelLockCollection
{
    public const string Name = "TunnelLock";
}

public sealed class TunnelLockCollectionGuardTests
{
    [Fact]
    public void La_coleccion_TunnelLock_no_se_paraleliza()
    {
        var def = typeof(TunnelLockCollection).GetCustomAttribute<CollectionDefinitionAttribute>();

        def.Should().NotBeNull();
        def!.DisableParallelization.Should().BeTrue();
    }

    [Fact]
    public void Las_clases_que_acortan_el_timeout_del_lock_estan_en_la_coleccion()
    {
        var attr = typeof(TunnelHandlerR3Tests).GetCustomAttributesData()
            .SingleOrDefault(a => a.AttributeType == typeof(CollectionAttribute));

        attr.Should().NotBeNull();
        attr!.ConstructorArguments[0].Value.Should().Be(TunnelLockCollection.Name);
    }
}
