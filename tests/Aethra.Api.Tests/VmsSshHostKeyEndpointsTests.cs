using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Aethra.Modules.Vms.Presentation;
using Aethra.Modules.Vms.UseCases.Dtos;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Aethra.Api.Tests;

public sealed class VmsSshHostKeyEndpointsTests(VmsSshHostKeyApiFactory factory)
    : IClassFixture<VmsSshHostKeyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ProbeSshHostKey_without_auth_returns_unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/vms/ssh/probe", new { host = "10.0.0.5", port = 22 });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RotateVmHostKey_without_auth_returns_unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/vms/vm_01JTESTVM00000000000000/ssh/rotate-host-key",
            new { newFingerprint = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProbeSshHostKey_with_read_scope_returns_ok_probe_result()
    {
        factory.Mediator.Send(Arg.Any<ProbeVmHostKeyQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProbeVmHostKeyResult>.Success(new ProbeVmHostKeyResult(
                Host: "10.0.0.5",
                Port: 22,
                HostKeyType: "ssh-ed25519",
                Fingerprint: "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU",
                MatchesPinned: true,
                PinnedFingerprint: "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU",
                Success: true)));

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/vms/ssh/probe")
        {
            Content = JsonContent.Create(new { host = "10.0.0.5", port = 22 }),
        };
        req.Headers.Add(VmsSshHostKeyApiFactory.CallerHeader, "read-caller");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ProbeVmHostKeyResult>();
        body.Should().NotBeNull();
        body!.Fingerprint.Should().Be("SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU");
        body.MatchesPinned.Should().BeTrue();
    }

    [Fact]
    public async Task RotateVmHostKey_with_write_scope_returns_ok_updated_vm()
    {
        factory.Mediator.Send(Arg.Any<RotateVmHostKeyCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<VmDto>.Success(new VmDto(
                Id: "vm_01JTESTVM00000000000000",
                Slug: "test-vm",
                Name: "Test VM",
                PublicIp: "10.0.0.5",
                PrivateIp: null,
                Description: null,
                Status: "Pending",
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                LastConnectedAt: null,
                LastDisconnectedAt: null,
                Hostname: null,
                KernelVersion: null,
                CpuModel: null,
                CpuCores: null,
                TotalMemoryBytes: null,
                AgentVersion: null,
                AcceptsPreviews: true,
                SshHostKeyFingerprint: "SHA256:uD34F12345678901234567890123456789012345678")));

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/vms/vm_01JTESTVM00000000000000/ssh/rotate-host-key")
        {
            Content = JsonContent.Create(new { newFingerprint = "SHA256:uD34F12345678901234567890123456789012345678" }),
        };
        req.Headers.Add(VmsSshHostKeyApiFactory.CallerHeader, "write-caller");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<VmDto>();
        body.Should().NotBeNull();
        body!.SshHostKeyFingerprint.Should().Be("SHA256:uD34F12345678901234567890123456789012345678");
    }

    [Fact]
    public async Task AutoInstall_with_mismatched_key_returns_conflict()
    {
        factory.Mediator.Send(Arg.Any<AutoInstallSatelliteCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<AutoInstallSatelliteResult>.Failure(
                Error.Conflict("ssh.host_key_mismatch", "La huella esperada no coincide con la registrada")));

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/vms/vm_01JTESTVM00000000000000/install/auto")
        {
            Content = JsonContent.Create(new VmsEndpoints.AutoInstallRequest(
                Ssh: new VmsEndpoints.SshBlock("10.0.0.5", 22, "root", "password", "pass", "SHA256:BADKEY"),
                InstallContainerRuntime: false,
                ContainerRuntime: "docker",
                DryRun: false,
                ExpectedHostKeyFingerprint: "SHA256:BADKEY",
                RotateHostKey: false)),
        };
        req.Headers.Add(VmsSshHostKeyApiFactory.CallerHeader, "write-caller");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}

public sealed class VmsSshHostKeyApiFactory : AethraApiFactory
{
    public const string CallerHeader = "X-Test-Vms-Caller";
    public IMediator Mediator { get; } = Substitute.For<IMediator>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMediator>();
            services.AddSingleton(Mediator);

            services.RemoveAll<IAuthenticationService>();
            services.AddScoped<IAuthenticationService, HeaderAuthenticationService>();
        });
    }

    private sealed class HeaderAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var caller = context.Request.Headers[CallerHeader].ToString();
            var requestedScheme = scheme ?? ApiKeyAuthSchemes.CookieScheme;
            if (string.IsNullOrEmpty(caller))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "test-user"),
                new(ApiKeyAuthSchemes.ApiKeyIdClaim, "apk_test"),
            };

            if (caller == "read-caller")
            {
                claims.Add(new(ApiKeyAuthSchemes.ScopeClaim, "vms:read"));
            }
            else if (caller == "write-caller")
            {
                claims.Add(new(ApiKeyAuthSchemes.ScopeClaim, "vms:read"));
                claims.Add(new(ApiKeyAuthSchemes.ScopeClaim, "vms:write"));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, requestedScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, requestedScheme)));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
