using System.Reflection;
using System.Text;
using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

public sealed class TunnelHandlerR3Tests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ensure_with_null_arguments_uses_oldest_central_tunnel_and_keeps_existing_host_as_noop()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now.AddMinutes(-10), "vm-bogota");
        var central = AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(direct.TunnelId, [new TunnelIngressRule(null, "http_status:404")]);
        api.SetIngress(central.TunnelId, [
            new TunnelIngressRule("app.example.com", "http://legacy-service:8080"),
            new TunnelIngressRule(null, central.FallbackService, central.FallbackNoTlsVerify)
        ]);

        var result = await SendEnsureAsync(
            db, api, new EnsureTunnelHostnameCommand("app.example.com"));

        result.IsSuccess.Should().BeTrue();
        api.PutCalls.Should().BeEmpty();
        api.GetCalls.Should().Equal("central-tun");
    }

    [Fact]
    public async Task Ensure_with_null_arguments_inserts_on_central_even_when_direct_is_older()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now.AddMinutes(-10), "vm-bogota");
        var central = AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(direct.TunnelId, [new TunnelIngressRule(null, "http_status:404")]);
        api.SetIngress(central.TunnelId, [new TunnelIngressRule(null, central.FallbackService, central.FallbackNoTlsVerify)]);

        var result = await SendEnsureAsync(
            db, api, new EnsureTunnelHostnameCommand("app.example.com"));

        result.IsSuccess.Should().BeTrue();
        api.PutCalls.Should().Equal("central-tun");
        api.GetCalls.Should().Equal("central-tun", "central-tun");
        api.IngressFor(central.TunnelId)
            .Should()
            .ContainSingle(r => r.Hostname == "app.example.com" && r.Service == central.AethraService);
        api.IngressFor(direct.TunnelId)
            .Should()
            .ContainSingle(r => r.Hostname == null);
    }

    [Fact]
    public async Task Ensure_with_explicit_direct_tunnel_replaces_existing_host_and_verifies_after_put()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now, "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(direct.TunnelId, [
            new TunnelIngressRule("app.example.com", "http://old:8080"),
            new TunnelIngressRule(null, "http_status:404")
        ]);

        var result = await SendEnsureAsync(
            db,
            api,
            new EnsureTunnelHostnameCommand("app.example.com", direct.TunnelId, "http://app:3000"));

        result.IsSuccess.Should().BeTrue();
        api.PutCalls.Should().Equal("direct-tun");
        api.IngressFor(direct.TunnelId)
            .Should()
            .ContainSingle(r => r.Hostname == "app.example.com" && r.Service == "http://app:3000");
    }

    [Fact]
    public async Task Ensure_with_explicit_central_tunnel_does_not_replace_existing_host()
    {
        await using var db = NewDb();
        var central = AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(central.TunnelId, [
            new TunnelIngressRule("app.example.com", "http://old:8080"),
            new TunnelIngressRule(null, central.FallbackService, central.FallbackNoTlsVerify)
        ]);

        var result = await SendEnsureAsync(
            db,
            api,
            new EnsureTunnelHostnameCommand("app.example.com", central.TunnelId, "http://app:3000"));

        result.IsSuccess.Should().BeTrue();
        api.PutCalls.Should().BeEmpty();
        api.IngressFor(central.TunnelId)
            .Should()
            .ContainSingle(r => r.Hostname == "app.example.com" && r.Service == "http://old:8080");
    }

    [Fact]
    public async Task Ensure_fails_when_rule_is_missing_after_put_reread()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now, "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi { ApplyPuts = false };
        api.SetIngress(direct.TunnelId, [new TunnelIngressRule(null, "http_status:404")]);

        var result = await SendEnsureAsync(
            db,
            api,
            new EnsureTunnelHostnameCommand("app.example.com", direct.TunnelId, "http://app:3000"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("cloudflare.ingress_not_applied");
    }

    [Fact]
    public async Task Ensure_and_remove_with_explicit_missing_tunnel_return_not_found()
    {
        await using var db = NewDb();
        var api = new FakeCloudflareApi();

        var ensure = await SendEnsureAsync(
            db,
            api,
            new EnsureTunnelHostnameCommand("app.example.com", "missing-tun", "http://app:3000"));
        var remove = await SendRemoveAsync(
            db,
            api,
            new RemoveTunnelHostnameCommand("app.example.com", "missing-tun"));

        ensure.IsFailure.Should().BeTrue();
        ensure.Error.Code.Should().Be("tunnel.not_found");
        remove.IsFailure.Should().BeTrue();
        remove.Error.Code.Should().Be("tunnel.not_found");
        api.GetCalls.Should().BeEmpty();
        api.PutCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_with_null_argument_removes_from_central_tunnel_only_and_verifies_after_put()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now.AddMinutes(-10), "vm-bogota");
        var central = AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(direct.TunnelId, [
            new TunnelIngressRule("app.example.com", "http://app:3000"),
            new TunnelIngressRule(null, "http_status:404")
        ]);
        api.SetIngress(central.TunnelId, [
            new TunnelIngressRule("app.example.com", central.AethraService),
            new TunnelIngressRule(null, central.FallbackService, central.FallbackNoTlsVerify)
        ]);

        var result = await SendRemoveAsync(
            db, api, new RemoveTunnelHostnameCommand("app.example.com"));

        result.IsSuccess.Should().BeTrue();
        api.PutCalls.Should().Equal("central-tun");
        api.GetCalls.Should().Equal("central-tun", "central-tun");
        api.IngressFor(central.TunnelId)
            .Should()
            .NotContain(r => r.Hostname == "app.example.com");
        api.IngressFor(direct.TunnelId)
            .Should()
            .ContainSingle(r => r.Hostname == "app.example.com");
    }

    [Fact]
    public async Task Remove_fails_when_host_is_still_present_after_put_reread()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now, "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi { ApplyPuts = false };
        api.SetIngress(direct.TunnelId, [
            new TunnelIngressRule("app.example.com", "http://app:3000"),
            new TunnelIngressRule(null, "http_status:404")
        ]);

        var result = await SendRemoveAsync(
            db, api, new RemoveTunnelHostnameCommand("app.example.com", direct.TunnelId));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("cloudflare.ingress_not_applied");
    }

    [Fact]
    public async Task RunSerializedAsync_returns_timeout_when_tunnel_lock_is_held()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now, "vm-bogota");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = SendRunSerializedAsync(
            direct,
            async () =>
            {
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
                return Result.Success();
            },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        (await Task.WhenAny(entered.Task, Task.Delay(TimeSpan.FromSeconds(1)))).Should().Be(entered.Task);

        var second = await SendRunSerializedAsync(
            direct,
            () => Task.FromResult(Result.Success()),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("cloudflare.ingress_lock_timeout");
        release.SetResult();
        (await first).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Ensure_with_null_tunnel_id_propagates_cancellation_while_waiting_for_ingress_lock()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using (var seed = NewDb(dbName))
        {
            AddTunnel(seed, "central-tun", TunnelMode.CentralProxy, Now);
            await seed.SaveChangesAsync();
        }

        await using var firstDb = NewDb(dbName);
        await using var secondDb = NewDb(dbName);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockFirstGet = true;
        var api = new FakeCloudflareApi
        {
            OnGetAsync = _ =>
            {
                if (!blockFirstGet)
                {
                    return Task.CompletedTask;
                }

                blockFirstGet = false;
                entered.SetResult();
                return release.Task;
            }
        };
        api.SetIngress("central-tun", [new TunnelIngressRule(null, "https://localhost:443")]);

        var first = SendEnsureAsync(
            firstDb,
            api,
            new EnsureTunnelHostnameCommand("one.example.com"),
            CancellationToken.None);
        (await Task.WhenAny(entered.Task, Task.Delay(TimeSpan.FromSeconds(1)))).Should().Be(entered.Task);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var second = async () => await SendEnsureAsync(
            secondDb,
            api,
            new EnsureTunnelHostnameCommand("two.example.com"),
            cts.Token);

        await second.Should().ThrowAsync<OperationCanceledException>();

        release.SetResult();
        (await first).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SetIngress_fails_when_rules_are_missing_after_put_reread()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now, "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi { ApplyPuts = false };
        api.SetIngress(direct.TunnelId, [new TunnelIngressRule(null, "http_status:404")]);

        var result = await SendSetAsync(
            db,
            api,
            new SetTunnelIngressCommand(
                [
                    new TunnelIngressRule("app.example.com", "http://app:3000"),
                    new TunnelIngressRule(null, "http_status:404")
                ],
                direct.TunnelId));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("cloudflare.ingress_not_applied");
    }

    [Fact]
    public async Task GetTunnel_and_connector_token_with_null_arguments_use_oldest_central_tunnel()
    {
        await using var db = NewDb();
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now.AddMinutes(-10), "vm-bogota");
        var central = AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(direct.TunnelId, [new TunnelIngressRule(null, "http_status:404")]);
        api.SetIngress(central.TunnelId, [new TunnelIngressRule(null, central.FallbackService, central.FallbackNoTlsVerify)]);

        var tunnel = await SendGetTunnelAsync(db, api, new GetTunnelQuery());
        var token = await SendGetConnectorTokenAsync(db, api, new GetConnectorTokenQuery());

        tunnel.IsSuccess.Should().BeTrue();
        tunnel.Value!.TunnelId.Should().Be("central-tun");
        token.IsSuccess.Should().BeTrue();
        token.Value.Should().Be("connector:central-tun");
    }

    [Fact]
    public async Task GetTunnel_and_connector_token_with_explicit_tunnel_id_can_use_direct_tunnel()
    {
        await using var db = NewDb();
        AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now.AddMinutes(1), "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(direct.TunnelId, [new TunnelIngressRule(null, "http_status:404")]);

        var tunnel = await SendGetTunnelAsync(db, api, new GetTunnelQuery(direct.TunnelId));
        var token = await SendGetConnectorTokenAsync(db, api, new GetConnectorTokenQuery(direct.TunnelId));

        tunnel.IsSuccess.Should().BeTrue();
        tunnel.Value!.TunnelId.Should().Be("direct-tun");
        tunnel.Value.Mode.Should().Be("direct");
        token.IsSuccess.Should().BeTrue();
        token.Value.Should().Be("connector:direct-tun");
    }

    [Fact]
    public async Task Delete_with_null_argument_removes_central_tunnel_only()
    {
        await using var db = NewDb();
        AddTunnel(db, "direct-tun", TunnelMode.Direct, Now.AddMinutes(-10), "vm-bogota");
        AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        await db.SaveChangesAsync();

        var result = await SendDeleteAsync(db, new DeleteTunnelCommand());

        result.IsSuccess.Should().BeTrue();
        (await db.Tunnels.Select(t => t.TunnelId).ToListAsync())
            .Should()
            .Equal("direct-tun");
    }

    [Fact]
    public async Task GetTunnelForVm_returns_direct_tunnel_for_target_vm_only()
    {
        await using var db = NewDb();
        AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now);
        var direct = AddTunnel(db, "direct-tun", TunnelMode.Direct, Now.AddMinutes(1), "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        api.SetIngress(direct.TunnelId, [new TunnelIngressRule(null, "http_status:404")]);

        var result = await SendGetTunnelForVmAsync(db, api, new GetTunnelForVmQuery(" vm-bogota "));

        result.IsSuccess.Should().BeTrue();
        result.Value!.TunnelId.Should().Be("direct-tun");
        result.Value.Mode.Should().Be("direct");
    }

    [Fact]
    public async Task GetTunnelForVm_with_include_ingress_false_does_not_call_cloudflare_api()
    {
        await using var db = NewDb();
        AddTunnel(db, "direct-tun", TunnelMode.Direct, Now, "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();

        var result = await SendGetTunnelForVmAsync(
            db,
            api,
            new GetTunnelForVmQuery("vm-bogota", IncludeIngress: false));

        result.IsSuccess.Should().BeTrue();
        result.Value!.TunnelId.Should().Be("direct-tun");
        result.Value.Ingress.Should().BeEmpty();
        api.GetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTunnelForVm_with_include_ingress_false_does_not_decode_token()
    {
        await using var db = NewDb();
        AddTunnel(db, "direct-tun", TunnelMode.Direct, Now, "vm-bogota");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();

        var result = await SendGetTunnelForVmAsync(
            db,
            api,
            new GetTunnelForVmQuery("vm-bogota", IncludeIngress: false),
            new ThrowingDecodeTokenCodec());

        result.IsSuccess.Should().BeTrue();
        result.Value!.TunnelId.Should().Be("direct-tun");
        result.Value.Ingress.Should().BeEmpty();
        api.GetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCentralProxyTunnel_with_include_ingress_false_does_not_decode_token()
    {
        await using var db = NewDb();
        AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now, "vm_ET39W0FFRCCQS60DY90B516MPR");
        await db.SaveChangesAsync();

        var result = await SendGetCentralProxyTunnelAsync(
            db,
            new FakeCloudflareApi(),
            new GetCentralProxyTunnelQuery(IncludeIngress: false),
            new ThrowingDecodeTokenCodec());

        result.IsSuccess.Should().BeTrue();
        result.Value!.TunnelId.Should().Be("central-tun");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("direct-tun", true)]
    public void Lock_timeout_applies_only_with_explicit_tunnel_id(string? tunnelId, bool expectsTimeout)
    {
        var support = typeof(CloudflareTunnel).Assembly
            .GetType("Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands.TunnelIngressSupport")!;
        var timeout = (TimeSpan?)support
            .GetMethod("LockTimeoutFor", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, [tunnelId]);

        timeout.HasValue.Should().Be(expectsTimeout);
    }

    [Fact]
    public async Task GetCentralProxyTunnel_with_include_ingress_false_returns_target_vm_without_cloudflare_api()
    {
        await using var db = NewDb();
        AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now, "vm_ET39W0FFRCCQS60DY90B516MPR");
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();

        var result = await SendGetCentralProxyTunnelAsync(
            db,
            api,
            new GetCentralProxyTunnelQuery(IncludeIngress: false));

        result.IsSuccess.Should().BeTrue();
        result.Value!.TunnelId.Should().Be("central-tun");
        result.Value.TargetVmId.Should().Be("vm_ET39W0FFRCCQS60DY90B516MPR");
        result.Value.Ingress.Should().BeEmpty();
        api.GetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTunnelForVm_ignores_production_central_proxy_tunnel_targeting_principal()
    {
        await using var db = NewDb();
        AddTunnel(db, "central-tun", TunnelMode.CentralProxy, Now, "principal");
        await db.SaveChangesAsync();

        var result = await SendGetTunnelForVmAsync(
            db,
            new FakeCloudflareApi(),
            new GetTunnelForVmQuery("principal"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    private static CloudflareDbContext NewDb(string? databaseName = null)
        => new(new DbContextOptionsBuilder<CloudflareDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static CloudflareTunnel AddTunnel(
        CloudflareDbContext db,
        string tunnelId,
        TunnelMode mode,
        DateTimeOffset createdAt,
        string? targetVmId = null)
    {
        var tunnel = CloudflareTunnel.Create(
            tunnelId,
            tunnelId,
            "acct-1",
            Encoding.UTF8.GetBytes($"api:{tunnelId}"),
            "http://localhost:5080",
            mode == TunnelMode.Direct ? "http_status:404" : "https://localhost:443",
            false,
            createdAt,
            mode,
            targetVmId);
        db.Tunnels.Add(tunnel);
        return tunnel;
    }

    private static Task<Result> SendEnsureAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        EnsureTunnelHostnameCommand command,
        CancellationToken cancellationToken = default)
        => InvokeCommandAsync(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands.EnsureTunnelHostnameHandler",
            [db, api, new PlainTextTokenCodec(), new FixedClock(Now.AddMinutes(5))],
            command,
            cancellationToken);

    private static Task<Result> SendRemoveAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        RemoveTunnelHostnameCommand command)
        => InvokeCommandAsync(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands.RemoveTunnelHostnameHandler",
            [db, api, new PlainTextTokenCodec(), new FixedClock(Now.AddMinutes(5))],
            command);

    private static Task<Result> SendSetAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        SetTunnelIngressCommand command)
        => InvokeCommandAsync(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands.SetTunnelIngressHandler",
            [db, api, new PlainTextTokenCodec(), new FixedClock(Now.AddMinutes(5))],
            command);

    private static Task<Result> SendDeleteAsync(CloudflareDbContext db, DeleteTunnelCommand command)
        => InvokeCommandAsync(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands.DeleteTunnelHandler",
            [db],
            command);

    private static Task<Result<CloudflareTunnelDto?>> SendGetTunnelAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        GetTunnelQuery query)
        => InvokeQueryAsync<CloudflareTunnelDto?>(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries.GetTunnelHandler",
            [db, api, new PlainTextTokenCodec()],
            query);

    private static Task<Result<string>> SendGetConnectorTokenAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        GetConnectorTokenQuery query)
        => InvokeQueryAsync<string>(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries.GetConnectorTokenHandler",
            [db, api, new PlainTextTokenCodec()],
            query);

    private static Task<Result<CloudflareTunnelDto?>> SendGetTunnelForVmAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        GetTunnelForVmQuery query,
        ICloudflareTokenCodec? codec = null)
        => InvokeQueryAsync<CloudflareTunnelDto?>(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries.GetTunnelForVmHandler",
            [db, api, codec ?? new PlainTextTokenCodec()],
            query);

    private static Task<Result<CloudflareTunnelDto?>> SendGetCentralProxyTunnelAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        GetCentralProxyTunnelQuery query,
        ICloudflareTokenCodec? codec = null)
        => InvokeQueryAsync<CloudflareTunnelDto?>(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries.GetCentralProxyTunnelHandler",
            [db, api, codec ?? new PlainTextTokenCodec()],
            query);

    private static async Task<Result> SendRunSerializedAsync(
        CloudflareTunnel tunnel,
        Func<Task<Result>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var type = typeof(EnsureTunnelHostnameCommand).Assembly.GetType(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands.TunnelIngressSupport",
            throwOnError: true)!;
        var task = (Task<Result>)type
            .GetMethod("RunSerializedAsync", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, [tunnel, action, cancellationToken, timeout])!;
        return await task.ConfigureAwait(false);
    }

    private static async Task<Result> InvokeCommandAsync<TCommand>(
        string handlerTypeName,
        object[] dependencies,
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        var handler = CreateHandler(handlerTypeName, dependencies);
        var task = (Task<Result>)handler.GetType()
            .GetMethod("Handle", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(handler, [command!, cancellationToken])!;
        return await task.ConfigureAwait(false);
    }

    private static async Task<Result<T>> InvokeQueryAsync<T>(
        string handlerTypeName,
        object[] dependencies,
        object query)
    {
        var handler = CreateHandler(handlerTypeName, dependencies);
        var task = (Task<Result<T>>)handler.GetType()
            .GetMethod("Handle", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(handler, [query, CancellationToken.None])!;
        return await task.ConfigureAwait(false);
    }

    private static object CreateHandler(string handlerTypeName, object[] dependencies)
    {
        var type = typeof(EnsureTunnelHostnameCommand).Assembly.GetType(handlerTypeName, throwOnError: true)!;
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: dependencies,
            culture: null)!;
    }

    private sealed class PlainTextTokenCodec : ICloudflareTokenCodec
    {
        public byte[] Encode(string token) => Encoding.UTF8.GetBytes(token);

        public string Decode(byte[] cipher) => Encoding.UTF8.GetString(cipher);
    }

    private sealed class ThrowingDecodeTokenCodec : ICloudflareTokenCodec
    {
        public byte[] Encode(string token) => Encoding.UTF8.GetBytes(token);

        public string Decode(byte[] cipher) => throw new InvalidOperationException("Decode should not be called.");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeCloudflareApi : ICloudflareApiClient
    {
        private readonly Dictionary<string, List<TunnelIngressRule>> _ingress = new(StringComparer.Ordinal);

        public bool ApplyPuts { get; init; } = true;

        public List<string> GetCalls { get; } = [];

        public List<string> PutCalls { get; } = [];

        public Func<string, Task>? OnGetAsync { get; init; }

        public void SetIngress(string tunnelId, IReadOnlyList<TunnelIngressRule> rules)
            => _ingress[tunnelId] = rules.ToList();

        public List<TunnelIngressRule> IngressFor(string tunnelId) => _ingress[tunnelId];

        public Task<IReadOnlyList<TunnelIngressRule>> GetTunnelIngressAsync(
            string accountId,
            string tunnelId,
            string apiToken,
            CancellationToken cancellationToken)
        {
            GetCalls.Add(tunnelId);
            var delay = OnGetAsync?.Invoke(tunnelId) ?? Task.CompletedTask;
            if (!delay.IsCompletedSuccessfully)
            {
                return CompleteGetAfterDelayAsync(delay, tunnelId);
            }

            return Task.FromResult<IReadOnlyList<TunnelIngressRule>>(
                _ingress.TryGetValue(tunnelId, out var rules) ? rules.ToList() : []);
        }

        private async Task<IReadOnlyList<TunnelIngressRule>> CompleteGetAfterDelayAsync(Task delay, string tunnelId)
        {
            await delay.ConfigureAwait(false);
            return _ingress.TryGetValue(tunnelId, out var rules) ? rules.ToList() : [];
        }

        public Task PutTunnelIngressAsync(
            string accountId,
            string tunnelId,
            string apiToken,
            IReadOnlyList<TunnelIngressRule> ingress,
            CancellationToken cancellationToken)
        {
            PutCalls.Add(tunnelId);
            if (ApplyPuts)
            {
                _ingress[tunnelId] = ingress.ToList();
            }

            return Task.CompletedTask;
        }

        public Task<string> GetTunnelConnectorTokenAsync(
            string accountId,
            string tunnelId,
            string apiToken,
            CancellationToken cancellationToken)
            => Task.FromResult($"connector:{tunnelId}");

        public Task<CloudflareZoneInfo> GetZoneAsync(
            string zoneId,
            string apiToken,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CloudflareDnsRecordInfo>> ListDnsRecordsAsync(
            string zoneId,
            string apiToken,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> CreateDnsRecordAsync(
            string zoneId,
            string apiToken,
            CreateDnsRecordRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdateDnsRecordAsync(
            string zoneId,
            string externalRecordId,
            string apiToken,
            UpdateDnsRecordRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteDnsRecordAsync(
            string zoneId,
            string externalRecordId,
            string apiToken,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
