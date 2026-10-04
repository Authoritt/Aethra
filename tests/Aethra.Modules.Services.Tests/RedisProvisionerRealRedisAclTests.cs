using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Aethra.Modules.Services.Tests;

public sealed class RedisProvisionerRealRedisAclTests : IAsyncLifetime
{
    private const string AdminPassword = "admin-secret";
    private readonly string _containerName = "aethra-redis-acl-" + Guid.NewGuid().ToString("N")[..12];
    private int _port;
    private IConnectionMultiplexer? _admin;

    /// <summary>
    /// Motivo por el que NO hay runtime de contenedores, o <c>null</c> si lo hay. Existe para que la
    /// ausencia de Podman se reporte como SALTADO y no como rojo: acoplar <c>dotnet test</c> a que la
    /// maquina tenga un runtime rompe la suite para todo el mundo, y este runtime ya se demostro
    /// fragil (2026-09-18: la maquina WSL existia pero su conexion no estaba registrada, asi que
    /// <c>podman machine start</c> fallaba). Saltado NO es verde silencioso: xUnit lo cuenta aparte
    /// y el piso de la OT declara cuantos saltados son aceptables.
    /// </summary>
    private string? _runtimeUnavailable;

    [RequiresContainerRuntimeTheory]
    [InlineData(BindingPermissions.Owner, true, true)]
    [InlineData(BindingPermissions.ReadWrite, true, true)]
    [InlineData(BindingPermissions.ReadOnly, false, false)]
    public async Task Redis_acl_enforces_full_binding_permission_scale(
        BindingPermissions permissions,
        bool canWrite,
        bool canPublish)
    {
        var prefix = "app" + permissions.ToString().ToLowerInvariant();
        var username = "acl_" + permissions.ToString().ToLowerInvariant() + "_" + Guid.NewGuid().ToString("N")[..8];
        var password = "secret-" + Guid.NewGuid().ToString("N");

        var service = ManagedService.Adopt("redis", "Redis", ServiceType.Redis, "7", "vm_1", "redis",
            "redis:7-alpine", _port, "aethra-net", [1], DateTimeOffset.UnixEpoch);
        var binding = ServiceBinding.Create(service.Id, "inst_1", prefix, [2],
            permissions, null, null, DateTimeOffset.UnixEpoch);

        var provisioner = new RedisProvisioner(new LocalRedisResolver(), new StaticAdminCodec(),
            NullLogger<RedisProvisioner>.Instance);
        var outcome = await provisioner.ProvisionAsync(
            service,
            binding,
            new BindingCredentials(username, password),
            CancellationToken.None);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        await using var mux = await ConnectAsync(username, password);
        var db = mux.GetDatabase();
        var adminDb = _admin!.GetDatabase();
        var insideKey = prefix + ":k";
        var outsideKey = "other:" + prefix + ":k";
        var insideChannel = prefix + ":chan";
        var outsideChannel = "other:" + prefix + ":chan";

        await db.PingAsync();
        await adminDb.StringSetAsync(insideKey, "seed");
        await adminDb.StringSetAsync(outsideKey, "outside");

        (await db.StringGetAsync(insideKey)).Should().Be("seed");

        if (canWrite)
        {
            (await db.StringSetAsync(insideKey, "v", TimeSpan.FromSeconds(30), When.NotExists)).Should().BeFalse();
            (await db.StringSetAsync(prefix + ":new", "v", TimeSpan.FromSeconds(30), When.NotExists)).Should().BeTrue();
            (await db.KeyDeleteAsync(prefix + ":new")).Should().BeTrue();
        }
        else
        {
            await ShouldBeDeniedAsync(async () => await db.StringSetAsync(prefix + ":new", "v", TimeSpan.FromSeconds(30), When.NotExists));
            await ShouldBeDeniedAsync(async () => await db.KeyDeleteAsync(insideKey));
        }

        if (canPublish)
        {
            await mux.GetSubscriber().PublishAsync(RedisChannel.Literal(insideChannel), "hello");
        }
        else
        {
            await ShouldBeDeniedAsync(async () => await mux.GetSubscriber().PublishAsync(RedisChannel.Literal(insideChannel), "hello"));
        }

        await ShouldReceiveSubscribedMessageAsync(mux, insideChannel);
        await ShouldBeDeniedAsync(async () => await db.StringGetAsync(outsideKey));
        await ShouldBeDeniedAsync(async () => await mux.GetSubscriber().PublishAsync(RedisChannel.Literal(outsideChannel), "hello"));
        await ShouldBeDeniedAsync(async () => await mux.GetSubscriber().SubscribeAsync(RedisChannel.Literal(outsideChannel), (_, _) => { }));
        await ShouldBeDeniedAsync(async () => await db.ExecuteAsync("FLUSHDB"));

        // Lo que ningun nivel puede hacer sin @scripting: liberar su propio lock. El lease usa un
        // compare-and-delete en Lua, igual que el cache de Microsoft. Si esto se niega, el lock
        // adquiere y no libera — un fallo lento que no se ve hasta que la cola se para.
        var luaRead = "return redis.call('get', KEYS[1])";
        (await db.ScriptEvaluateAsync(luaRead, [insideKey])).Should().NotBeNull();

        // Y el script NO es una puerta trasera: el ACL del llamante se aplica dentro del Lua.
        var luaWrite = "return redis.call('set', KEYS[1], 'via-lua')";
        if (canWrite)
        {
            await db.ScriptEvaluateAsync(luaWrite, [insideKey]);
        }
        else
        {
            await ShouldBeDeniedAsync(async () => await db.ScriptEvaluateAsync(luaWrite, [insideKey]));
        }

        await ShouldBeDeniedAsync(async () => await db.ScriptEvaluateAsync(luaRead, [outsideKey]));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await StartRedisAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException
                                      or System.ComponentModel.Win32Exception)
        {
            // Sin runtime de contenedores (o roto): se declara y el test se salta, no se rompe.
            _runtimeUnavailable =
                "No hay runtime de contenedores utilizable para levantar redis:7-alpine: " + ex.Message;
        }
    }

    private async Task StartRedisAsync()
    {
        _port = GetFreePort();
        await RunPodmanAsync(
            "run",
            "--rm",
            "-d",
            "--name",
            _containerName,
            "-p",
            "127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + ":6379",
            "redis:7-alpine",
            "redis-server",
            "--requirepass",
            AdminPassword);

        _admin = await WaitForRedisAsync("default", AdminPassword);
    }

    public async Task DisposeAsync()
    {
        if (_admin is not null)
        {
            await _admin.DisposeAsync();
        }

        if (_runtimeUnavailable is not null)
        {
            return;
        }

        try
        {
            await RunPodmanAsync("rm", "-f", _containerName);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // El runtime desaparecio a mitad; no hay contenedor que limpiar ni nada que reportar aqui.
        }
    }

    private async Task ShouldReceiveSubscribedMessageAsync(IConnectionMultiplexer mux, string channel)
    {
        var received = new TaskCompletionSource<RedisValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = mux.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(channel), (_, value) => received.TrySetResult(value));
        try
        {
            await _admin!.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), "sub-ok");
            var value = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            value.Should().Be("sub-ok");
        }
        finally
        {
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(channel));
        }
    }

    private static async Task ShouldBeDeniedAsync(Func<Task> action)
    {
        var ex = await Record.ExceptionAsync(action);
        ex.Should().BeOfType<RedisServerException>();
        // Redis niega con DOS redacciones distintas y hay que aceptar las dos: "NOPERM ..." cuando
        // el comando llega directo, y "ERR ACL failure in script: ..." cuando el mismo comando va
        // dentro de un EVAL. Exigir solo la primera dejaba el caso del script sin comprobar de
        // verdad — que es justo donde se escondia el hueco de @scripting.
        ex!.Message.Should().Match(m => m.Contains("NOPERM") || m.Contains("ACL failure in script"));
    }

    private async Task<IConnectionMultiplexer> ConnectAsync(string username, string password)
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(BuildOptions(username, password));
        await mux.GetDatabase().PingAsync();
        return mux;
    }

    private async Task<IConnectionMultiplexer> WaitForRedisAsync(string username, string password)
    {
        Exception? last = null;
        for (var i = 0; i < 60; i++)
        {
            try
            {
                return await ConnectAsync(username, password);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("Redis container did not become reachable.", last);
    }

    private ConfigurationOptions BuildOptions(string username, string password)
    {
        var options = new ConfigurationOptions
        {
            User = username,
            Password = password,
            AbortOnConnectFail = false,
            ConnectTimeout = 2_000,
            SyncTimeout = 5_000,
            AllowAdmin = true,
        };
        options.EndPoints.Add(IPAddress.Loopback, _port);
        return options;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> RunPodmanAsync(params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "podman",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 && args is not ["rm", "-f", _])
        {
            throw new InvalidOperationException(
                "podman " + string.Join(' ', args) + " failed with exit " + process.ExitCode + ": " + stderr);
        }

        return stdout;
    }

    private sealed class LocalRedisResolver : IManagedServiceHostResolver
    {
        public Task<string> ResolveAsync(ManagedService service, CancellationToken cancellationToken)
            => Task.FromResult("127.0.0.1");

        public Task<int> ResolveManagementPortAsync(ManagedService service, CancellationToken cancellationToken)
            => Task.FromResult(service.InternalPort);
    }

    private sealed class StaticAdminCodec : IAdminCredentialsCodec
    {
        public byte[] Encode(AdminCredentials credentials) => [1];

        public AdminCredentials Decode(byte[] cipher) => new("default", AdminPassword);
    }
}
