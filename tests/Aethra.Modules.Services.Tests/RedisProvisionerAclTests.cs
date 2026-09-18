using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Binding;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Services;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

public sealed class RedisProvisionerAclTests
{
    [Fact]
    public void ReadWrite_acl_grants_connection_pubsub_and_write_only_inside_prefix()
    {
        var args = RedisProvisioner.BuildAclSetUser("app_user", "secret", "app", BindingPermissions.ReadWrite)
            .Select(x => x.ToString())
            .ToArray();

        args.Should().Contain(["~app:*", "&app:*", "+@connection", "+@pubsub", "+@read", "+@write"]);
        args.Should().NotContain("~*");
        args.Should().NotContain("&*");
    }

    [Fact]
    public void ReadOnly_acl_keeps_connection_and_subscribe_capability_without_write_category()
    {
        var args = RedisProvisioner.BuildAclSetUser("app_user", "secret", "app", BindingPermissions.ReadOnly)
            .Select(x => x.ToString())
            .ToArray();

        args.Should().Contain(["~app:*", "&app:*", "+@connection", "+@pubsub", "+@read", "-@write"]);
        args.Should().NotContain("+@write");
    }

    [Fact]
    public void Redis_env_vars_publish_the_same_key_and_channel_prefix_that_acl_grants()
    {
        var service = ManagedService.Adopt("redis", "Redis", ServiceType.Redis, "7", "vm_1", "redis",
            "redis:7-alpine", 6379, "aethra-net", [1], DateTimeOffset.UnixEpoch);
        var binding = ServiceBinding.Create(service.Id, "inst_1", "app", [2],
            BindingPermissions.ReadWrite, null, null, DateTimeOffset.UnixEpoch);
        var vars = new DefaultBindingEnvVarMapper()
            .Build(service, binding, new BindingCredentials("app_user", "secret"))
            .ToDictionary(v => v.Key, v => v.Value);

        vars["REDIS_PREFIX"].Should().Be("app:");
        vars["REDIS_KEY_PREFIX"].Should().Be("app:");
        vars["REDIS_CHANNEL_PREFIX"].Should().Be("app:");
    }
}
