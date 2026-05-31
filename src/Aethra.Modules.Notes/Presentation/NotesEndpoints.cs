using Aethra.Modules.Notes.Application.Commands;
using Aethra.Modules.Notes.Application.Queries;
using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notes.Presentation;

/// <summary>
/// Endpoints REST del módulo Notes. Convención de errores idéntica al resto de módulos:
/// 422 Validation, 404 NotFound, 409 Conflict.
///
/// Todos los endpoints requieren autenticación (sesión cookie). Para servir imágenes el
/// browser envía la cookie automáticamente porque <c>&lt;img src&gt;</c> hace fetch con credenciales
/// same-origin.
/// </summary>
public static class NotesEndpoints
{
    /// <summary>Tamaño máximo aceptado por upload (5 MB).</summary>
    public const long MaxImageSizeBytes = 5_000_000;

    /// <summary>Allowlist de MIME types aceptados para imágenes.</summary>
    private static readonly HashSet<string> AllowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    public static IEndpointRouteBuilder MapNotesEndpoints(this IEndpointRouteBuilder app)
    {
        var notes = app.MapGroup("/api/notes").WithTags("Notes").RequireAuthorization();

        notes.MapGet("/", async (
            [FromQuery(Name = "scope_type")] string scopeType,
            [FromQuery(Name = "scope_id")] string scopeId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!TryParseScope(scopeType, out var scope))
            {
                return Results.UnprocessableEntity(new { code = "note.invalid_scope", message = $"scope_type inválido: '{scopeType}'." });
            }
            var r = await mediator.Send(new ListNotesQuery(scope, scopeId), ct);
            return ToResult(r);
        }).WithName("ListNotes");

        notes.MapPost("/", async ([FromBody] CreateNoteRequest body, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseScope(body.ScopeType, out var scope))
            {
                return Results.UnprocessableEntity(new { code = "note.invalid_scope", message = $"scope_type inválido: '{body.ScopeType}'." });
            }
            var cmd = new CreateNoteCommand(scope, body.ScopeId, body.Title, body.MarkdownBody ?? string.Empty);
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/notes/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        }).WithName("CreateNote");

        notes.MapGet("/{noteId}", async (string noteId, IMediator mediator, CancellationToken ct) =>
            ToResult(await mediator.Send(new GetNoteByIdQuery(noteId), ct)))
            .WithName("GetNote");

        notes.MapPatch("/{noteId}", async (
            string noteId,
            [FromBody] UpdateNoteRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new UpdateNoteCommand(noteId, body.Title, body.MarkdownBody), ct);
            return ToResult(r);
        }).WithName("UpdateNote");

        notes.MapDelete("/{noteId}", async (string noteId, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new DeleteNoteCommand(noteId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteNote");

        notes.MapPost("/{noteId}/pin", async (
            string noteId,
            [FromBody] PinNoteRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new PinNoteCommand(noteId, body.Pinned), ct);
            return ToResult(r);
        }).WithName("PinNote");

        notes.MapPost("/{noteId}/images", async (
            string noteId,
            IFormFile file,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (file is null)
            {
                return Results.UnprocessableEntity(new { code = "note.image_missing", message = "Falta el archivo." });
            }
            if (file.Length <= 0)
            {
                return Results.UnprocessableEntity(new { code = "note.image_empty", message = "El archivo está vacío." });
            }
            if (file.Length > MaxImageSizeBytes)
            {
                return Results.UnprocessableEntity(new
                {
                    code = "note.image_too_large",
                    message = $"El archivo excede el límite de {MaxImageSizeBytes} bytes.",
                });
            }
            if (!AllowedImageMimeTypes.Contains(file.ContentType ?? string.Empty))
            {
                return Results.UnprocessableEntity(new
                {
                    code = "note.image_bad_mime",
                    message = $"Content-Type no admitido: '{file.ContentType}'. Use jpeg/png/webp/gif.",
                });
            }

            var sanitizedFilename = SanitizeFilename(file.FileName);
            await using var stream = file.OpenReadStream();
            var cmd = new UploadNoteImageCommand(
                NoteId: noteId,
                Filename: sanitizedFilename,
                ContentType: file.ContentType!,
                SizeBytes: (int)file.Length,
                Content: stream);
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/notes/{noteId}/images/{r.Value.ImageId:N}", r.Value)
                : MapError(r.Error);
        })
        .WithName("UploadNoteImage")
        .DisableAntiforgery();

        notes.MapDelete("/{noteId}/images/{imageId:guid}", async (
            string noteId,
            Guid imageId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var r = await mediator.Send(new DeleteNoteImageCommand(noteId, imageId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeleteNoteImage");

        // Servir el binario. Cache-Control: privado por 1h.
        notes.MapGet("/images/{imageId:guid}", async (
            Guid imageId,
            NotesDbContext db,
            INoteImageStore store,
            CancellationToken ct) =>
        {
            // Buscar metadata para obtener content-type. El owned table guarda image_id.
            var image = await db.Notes
                .AsNoTracking()
                .SelectMany(n => n.Images)
                .Where(i => i.Id == imageId)
                .Select(i => new { i.ContentType, i.OriginalFilename })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (image is null)
            {
                return Results.NotFound(new { code = "note.image_not_found", message = $"Imagen '{imageId}' no existe." });
            }

            var stream = await store.OpenReadAsync(imageId, ct).ConfigureAwait(false);
            if (stream is null)
            {
                return Results.NotFound(new { code = "note.image_blob_missing", message = "El binario asociado se perdió." });
            }

            return Results.File(
                fileStream: stream,
                contentType: image.ContentType,
                fileDownloadName: image.OriginalFilename,
                enableRangeProcessing: true);
        })
        .WithName("GetNoteImage")
        .WithMetadata(new ResponseCacheAttribute { Duration = 3600, Location = ResponseCacheLocation.Client });

        // ----------- Pinned Facts -----------
        var facts = app.MapGroup("/api/pinned-facts").WithTags("Notes").RequireAuthorization();

        facts.MapGet("/", async (
            [FromQuery(Name = "scope_type")] string scopeType,
            [FromQuery(Name = "scope_id")] string scopeId,
            [FromQuery(Name = "reveal")] bool? reveal,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!TryParseScope(scopeType, out var scope))
            {
                return Results.UnprocessableEntity(new { code = "pinned_fact.invalid_scope", message = $"scope_type inválido: '{scopeType}'." });
            }
            var q = new ListPinnedFactsQuery(scope, scopeId, reveal ?? false);
            return ToResult(await mediator.Send(q, ct));
        }).WithName("ListPinnedFacts");

        facts.MapPut("/", async ([FromBody] UpsertPinnedFactRequest body, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseScope(body.ScopeType, out var scope))
            {
                return Results.UnprocessableEntity(new { code = "pinned_fact.invalid_scope", message = $"scope_type inválido: '{body.ScopeType}'." });
            }
            var cmd = new UpsertPinnedFactCommand(
                ScopeType: scope,
                ScopeId: body.ScopeId,
                Key: body.Key,
                Value: body.Value,
                IsSecret: body.IsSecret,
                Description: body.Description);
            var r = await mediator.Send(cmd, ct);
            return ToResult(r);
        }).WithName("UpsertPinnedFact");

        facts.MapDelete("/{factId}", async (string factId, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new DeletePinnedFactCommand(factId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        }).WithName("DeletePinnedFact");

        return app;
    }

    // ---------- Request payloads ----------
    public sealed record CreateNoteRequest(string ScopeType, string ScopeId, string Title, string? MarkdownBody);
    public sealed record UpdateNoteRequest(string? Title, string? MarkdownBody);
    public sealed record PinNoteRequest(bool Pinned);
    public sealed record UpsertPinnedFactRequest(
        string ScopeType,
        string ScopeId,
        string Key,
        string Value,
        bool IsSecret,
        string? Description);

    // ---------- Helpers ----------
    private static bool TryParseScope(string? raw, out NoteScopeType scope)
        => Enum.TryParse(raw, ignoreCase: true, out scope) && Enum.IsDefined(scope);

    private static string SanitizeFilename(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "upload";
        }
        var name = Path.GetFileName(raw);
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[Math.Min(name.Length, 255)];
        var idx = 0;
        foreach (var c in name)
        {
            if (idx >= buffer.Length)
            {
                break;
            }
            buffer[idx++] = Array.IndexOf(invalid, c) >= 0 ? '_' : c;
        }
        var cleaned = new string(buffer[..idx]);
        return string.IsNullOrWhiteSpace(cleaned) ? "upload" : cleaned;
    }

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { code = e.Code, message = e.Message }),
        ErrorType.NotFound => Results.NotFound(new { code = e.Code, message = e.Message }),
        ErrorType.Conflict => Results.Conflict(new { code = e.Code, message = e.Message }),
        _ => Results.Problem(e.Message),
    };

    // Evita el warning CA1801 si AethraId no se usa en este archivo. Mantiene el patrón cross-module.
    static NotesEndpoints() => _ = AethraId.NewId("note");
}
