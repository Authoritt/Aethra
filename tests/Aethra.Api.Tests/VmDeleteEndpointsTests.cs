using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
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

public sealed class VmDeleteEndpointsTests(VmDeleteApiFactory factory)
    : IClassFixture<VmDeleteApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly IMediator _mediator = factory.Mediator;

    private const string TestVmId = "vm_01JTESTVM00000000000000";

    [Fact]
    public async Task DeleteVm_without_auth_returns_unauthorized()
    {
        var response = await _client.DeleteAsync($"/api/vms/{TestVmId}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteVm_with_read_scope_returns_forbidden()
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/vms/{TestVmId}");
        req.Headers.Add(VmDeleteApiFactory.CallerHeader, "read-caller");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteVm_when_vm_in_use_returns_conflict_409_with_code()
    {
        _mediator.Send(
            Arg.Is<DeleteVmCommand>(c => c.VmId == TestVmId && !c.Force),
            Arg.Any<CancellationToken>())
            .Returns(Error.Conflict("vm.in_use", "No se puede borrar la VM porque tiene cargas activas."));

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/vms/{TestVmId}");
        req.Headers.Add(VmDeleteApiFactory.CallerHeader, "write-caller");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("vm.in_use");
    }

    [Fact]
    public async Task DeleteVm_when_satellite_connected_and_no_force_returns_conflict_409()
    {
        _mediator.Send(
            Arg.Is<DeleteVmCommand>(c => c.VmId == TestVmId && !c.Force),
            Arg.Any<CancellationToken>())
            .Returns(Error.Conflict("vm.satellite_connected", "Satélite conectado. Pasa force=true."));

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/vms/{TestVmId}");
        req.Headers.Add(VmDeleteApiFactory.CallerHeader, "write-caller");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be("vm.satellite_connected");
    }

    [Fact]
    public async Task DeleteVm_when_successful_with_force_returns_no_content_204()
    {
        _mediator.Send(
            Arg.Is<DeleteVmCommand>(c => c.VmId == TestVmId && c.Force),
            Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/vms/{TestVmId}?force=true");
        req.Headers.Add(VmDeleteApiFactory.CallerHeader, "write-caller");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private sealed record ErrorResponse(string? Code, string? Message);
}

public sealed class VmDeleteApiFactory : AethraApiFactory
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

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => throw new NotSupportedException();
    }
}
