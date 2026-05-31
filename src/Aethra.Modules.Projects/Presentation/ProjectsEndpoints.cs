using Aethra.Modules.Projects.UseCases.Dtos;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Queries;
// DiscoverRepoCommand is in Commands too
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Projects.Presentation;

/// <summary>
/// Endpoints REST del módulo Projects. Convención:
/// - 200/201/204 para éxito.
/// - 401 sin sesión, 403 sin permiso (futuro).
/// - 404 NotFound, 409 Conflict, 422 Validation con problema-detalle.
/// - Todas las rutas requieren autenticación (RequireAuthorization).
/// </summary>
public static class ProjectsEndpoints
{
    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListProjectsQuery(), ct);
            return ToHttpResult(result);
        })
        .WithName("ListProjects")
        .WithDescription("Lista todos los proyectos con sus environments y applications.");

        group.MapGet("/{projectId}", async (string projectId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetProjectByIdQuery(projectId), ct);
            return ToHttpResult(result);
        })
        .WithName("GetProject")
        .WithDescription("Obtiene un proyecto por su ID con árbol completo.");

        group.MapPost("/", async (
            [FromBody] CreateProjectRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new CreateProjectCommand(
                Name: body.Name,
                Slug: body.Slug,
                Description: body.Description,
                Color: body.Color,
                Icon: body.Icon,
                DefaultEnvironment: body.DefaultEnvironment ?? "production");

            var result = await mediator.Send(cmd, ct);
            return result.IsSuccess
                ? Results.Created($"/api/projects/{result.Value.Id}", new
                {
                    project = result.Value,
                    next_actions = SuggestNextActions(result.Value),
                })
                : ToHttpResult(result);
        })
        .WithName("CreateProject")
        .WithDescription("Crea un proyecto con su environment default.");

        // Discover-repo: stub de F1, implementación real con clone en F4.
        app.MapPost("/api/projects/discover-repo", async (
                [FromBody] DiscoverRepoCommand body,
                IMediator mediator,
                CancellationToken ct) =>
            {
                var result = await mediator.Send(body, ct);
                return ToHttpResult(result);
            })
            .RequireAuthorization()
            .WithTags("Projects")
            .WithName("DiscoverRepo")
            .WithDescription("Analiza un repo Git y propone aplicaciones para crear.");

        return app;
    }

    /// <summary>
    /// Sugerencias para el cliente (UI o agente IA) sobre qué hacer después de crear un proyecto.
    /// </summary>
    private static object[] SuggestNextActions(ProjectDto p) =>
    [
        new
        {
            tool = "aethra_create_application_from_git",
            why = "Adjuntar una primera app al proyecto recién creado.",
            suggested_args = new { project_id = p.Id, environment_name = "production" },
        },
        new
        {
            tool = "aethra_set_env_vars",
            why = "Definir variables compartidas a nivel proyecto.",
            suggested_args = new { scope = "project", scope_id = p.Id, vars = Array.Empty<object>() },
        },
    ];

    private static IResult ToHttpResult<T>(Result<T> result)
        => result.IsSuccess
            ? Results.Ok(result.Value)
            : MapError(result.Error);

    private static IResult MapError(Error error) => error.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(BuildProblem(error, StatusCodes.Status422UnprocessableEntity)),
        ErrorType.NotFound => Results.NotFound(BuildProblem(error, StatusCodes.Status404NotFound)),
        ErrorType.Conflict => Results.Conflict(BuildProblem(error, StatusCodes.Status409Conflict)),
        ErrorType.Unauthorized => Results.Json(BuildProblem(error, StatusCodes.Status401Unauthorized),
            statusCode: StatusCodes.Status401Unauthorized),
        ErrorType.Forbidden => Results.Json(BuildProblem(error, StatusCodes.Status403Forbidden),
            statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError),
    };

    private static ProblemDetails BuildProblem(Error error, int status) => new()
    {
        Title = error.Code,
        Detail = error.Message,
        Status = status,
        Type = $"https://aethra.local/errors/{error.Code}",
    };
}

public sealed record CreateProjectRequest(
    string Name,
    string? Slug = null,
    string? Description = null,
    string? Color = null,
    string? Icon = null,
    string? DefaultEnvironment = null);
