using System.Security.Claims;
using Aethra.Shared.Contracts.Authentication;
using Microsoft.AspNetCore.Http;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Expone el principal que originó el comando. HTTP usa <see cref="IHttpContextAccessor"/>;
/// superficies que ejecutan fuera del request, como MCP, pueden propagar sus scopes durante
/// el envío a MediatR.
/// </summary>
public interface ICurrentPrincipalAccessor
{
    ClaimsPrincipal Current { get; }

    IDisposable UseScopes(IEnumerable<string> scopes);
}

internal sealed class CurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentPrincipalAccessor
{
    private readonly AsyncLocal<ClaimsPrincipal?> _override = new();

    public ClaimsPrincipal Current
        => _override.Value ?? httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public IDisposable UseScopes(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var previous = _override.Value;
        _override.Value = new ClaimsPrincipal(new ClaimsIdentity(
            scopes
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => new Claim(ApiKeyAuthSchemes.ScopeClaim, scope.Trim())),
            authenticationType: "propagated-scopes"));

        return new RestoreScope(this, previous);
    }

    private sealed class RestoreScope(
        CurrentPrincipalAccessor accessor,
        ClaimsPrincipal? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            accessor._override.Value = previous;
            _disposed = true;
        }
    }
}
