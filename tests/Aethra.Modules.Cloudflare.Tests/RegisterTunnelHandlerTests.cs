using System.Reflection;
using System.Text;
using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

public sealed class RegisterTunnelHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Register_existing_tunnel_without_mode_preserves_its_current_mode()
    {
        await using var db = NewDb();
        var tunnel = CloudflareTunnel.Create(
            "12345678-1234-1234-1234-123456789012",
            "authorit-bogota",
            "0123456789abcdef0123456789abcdef",
            Encoding.UTF8.GetBytes("old-token"),
            null,
            null,
            false,
            Now,
            TunnelMode.Direct,
            "vm-bogota");
        db.Tunnels.Add(tunnel);
        await db.SaveChangesAsync();

        var api = new FakeCloudflareApi();
        var command = new RegisterTunnelCommand(
            "0123456789abcdef0123456789abcdef",
            tunnel.TunnelId,
            "authorit-bogota",
            "new-token",
            null,
            null,
            false);

        var result = await SendRegisterAsync(db, api, command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Mode.Should().Be("direct");
        (await db.Tunnels.SingleAsync()).Mode.Should().Be(TunnelMode.Direct);
    }

    private static CloudflareDbContext NewDb()
        => new(new DbContextOptionsBuilder<CloudflareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<Result<CloudflareTunnelDto>> SendRegisterAsync(
        CloudflareDbContext db,
        FakeCloudflareApi api,
        RegisterTunnelCommand command)
    {
        var handler = CreateHandler(
            "Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands.RegisterTunnelHandler",
            [db, api, new PlainTextTokenCodec(), new FixedClock(Now.AddMinutes(5))]);
        var task = (Task<Result<CloudflareTunnelDto>>)handler.GetType()
            .GetMethod("Handle", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(handler, [command, CancellationToken.None])!;
        return await task.ConfigureAwait(false);
    }

    private static object CreateHandler(string handlerTypeName, object[] dependencies)
    {
        var type = typeof(RegisterTunnelCommand).Assembly.GetType(handlerTypeName, throwOnError: true)!;
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

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeCloudflareApi : ICloudflareApiClient
    {
        public Task<IReadOnlyList<TunnelIngressRule>> GetTunnelIngressAsync(
            string accountId,
            string tunnelId,
            string apiToken,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TunnelIngressRule>>([]);

        public Task PutTunnelIngressAsync(
            string accountId,
            string tunnelId,
            string apiToken,
            IReadOnlyList<TunnelIngressRule> ingress,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> GetTunnelConnectorTokenAsync(
            string accountId,
            string tunnelId,
            string apiToken,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

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
