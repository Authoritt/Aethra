using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Notes.Application.Commands;
using Aethra.Modules.Notes.Domain;
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
    [Description("Crea una Note markdown asociada a un scope (Project/Environment/Application). Opcionalmente upserts pinned-facts en el mismo scope.")]
    public async Task<object> AddNoteAsync(
        [Description("Scope: 'Project', 'Environment' o 'Application'.")] string scopeType,
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
                $"scope_type='{scopeType}' inválido. Use Project, Environment o Application.",
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
}
