using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.UseCases.Commands;
using Aethra.Modules.Identity.UseCases.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Identity.Presentation;

public static class ApiKeysEndpoints
{
    public static IEndpointRouteBuilder MapApiKeysEndpoints(this IEndpointRouteBuilder app)
    {
        // Gestión de API keys: SOLO via cookie. Una API key no puede crear otra API key.
        var group = app.MapGroup("/api/identity/api-keys")
            .WithTags("Identity")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = ApiKeyAuthSchemes.CookieScheme,
            });

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListApiKeysQuery(), ct)))
            .WithName("ListApiKeys");

        group.MapPost("/", async ([FromBody] CreateApiKeyRequest body, IMediator m, CancellationToken ct) =>
        {
            var cmd = new CreateApiKeyCommand(body.Name, body.Scopes, body.ExpiresAt);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/identity/api-keys/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        }).WithName("CreateApiKey");

        group.MapDelete("/{apiKeyId}", async (string apiKeyId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new RevokeApiKeyCommand(apiKeyId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("RevokeApiKey");

        return app;
    }

    public sealed record CreateApiKeyRequest(
        string Name,
        IReadOnlyList<string> Scopes,
        DateTimeOffset? ExpiresAt);

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { e.Code, e.Message }),
        ErrorType.NotFound => Results.NotFound(new { e.Code, e.Message }),
        ErrorType.Conflict => Results.Conflict(new { e.Code, e.Message }),
        _ => Results.Problem(e.Message),
    };
}
