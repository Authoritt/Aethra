using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Notes.Application.Commands;
using Aethra.Shared.Contracts.Notes;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

[McpServerToolType]
public sealed class NotesTools(IMediator mediator, IMcpCallerContext caller)
{
    public sealed record PinnedFactInput(
        string Key,
        string Value,
        bool IsSecret,
        string? Description);

    [McpServerTool(Name = "aethra_add_note", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Crea una Note markdown asociada a un scope (Project/Template/Client/Instance). Opcionalmente upserts pinned-facts en el mismo scope."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> AddNoteAsync(
        [Description("Scope: 'Project', 'Template', 'Client' o 'Instance'.")] string scopeType,
        [Description("ID del scope.")] string scopeId,
        [Description("Título de la nota.")] string title,
        [Description("Cuerpo markdown.")] string markdown,
        [Description("Pinned facts opcionales a upsertar simultáneamente.")] IReadOnlyList<PinnedFactInput>? pinnedFacts,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.NotesWrite);
        }
        if (!Enum.TryParse<NoteScopeType>(scopeType, ignoreCase: true, out var scope) || !Enum.IsDefined(scope))
        {
            return McpResponses.Failure("note.invalid_scope",
                $"scope_type='{scopeType}' inválido. Use Project, Template, Client o Instance.",
                "validation");
        }

        var noteResult = await mediator.Send(
            new CreateNoteCommand(scope, scopeId, title, markdown), ct).ConfigureAwait(false);
        if (!noteResult.IsSuccess)
        {
            return McpResponses.FromError(noteResult.Error);
        }

        var facts = new List<object>();
        if (pinnedFacts is not null)
        {
            foreach (var f in pinnedFacts)
            {
                var factResult = await mediator.Send(
                    new UpsertPinnedFactCommand(scope, scopeId, f.Key, f.Value, f.IsSecret, f.Description),
                    ct).ConfigureAwait(false);
                if (factResult.IsSuccess)
                {
                    facts.Add(new { ok = true, key = f.Key, id = factResult.Value.Id });
                }
                else
                {
                    facts.Add(new { ok = false, key = f.Key, error_code = factResult.Error.Code, error_message = factResult.Error.Message });
                }
            }
        }

        return McpResponses.Ok(new
        {
            note = noteResult.Value,
            pinned_facts = facts,
        });
    }

    [McpServerTool(Name = "aethra_update_note", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Actualiza (patch) el título y/o el cuerpo markdown de una nota. Sólo cambia los campos provistos; "
        + "los omitidos quedan igual. Devuelve el detalle de la nota. No toca los pinned facts del scope."
        + " [Sin dry_run: esta operacion se ejecuta de inmediato, no se puede simular.]")]
    public async Task<object> UpdateNoteAsync(
        [Description("ID de la nota (lo devuelve aethra_add_note).")] string noteId,
        [Description("Nuevo título. Omitir/null = no cambiar.")] string? title,
        [Description("Nuevo cuerpo markdown. Omitir/null = no cambiar.")] string? markdown,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.NotesWrite);
        }
        var result = await mediator.Send(new UpdateNoteCommand(noteId, title, markdown), ct).ConfigureAwait(false);
        return result.IsSuccess ? McpResponses.Ok(result.Value) : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_delete_note", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Elimina una nota. NO afecta los pinned facts del scope (esos se gestionan aparte). "
        + "Usá dry_run=true primero para confirmar.")]
    public async Task<object> DeleteNoteAsync(
        [Description("ID de la nota (lo devuelve aethra_add_note).")] string noteId,
        [Description("Si true, NO borra — devuelve el plan.")] bool dryRun,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.NotesWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.NotesWrite);
        }
        if (dryRun)
        {
            return McpResponses.DryRun(
                wouldCall: $"delete note {noteId}",
                plan: new { noteId, action = "delete note (pinned facts untouched)" });
        }
        var result = await mediator.Send(new DeleteNoteCommand(noteId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { note_id = noteId, deleted = true })
            : McpResponses.FromError(result.Error);
    }
}
