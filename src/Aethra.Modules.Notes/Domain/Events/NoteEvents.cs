using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Notes.Domain.Events;

public sealed record NoteCreatedEvent(NoteId NoteId, NoteScopeType ScopeType, string ScopeId, string Title) : DomainEvent;

public sealed record NoteUpdatedEvent(NoteId NoteId, string Title) : DomainEvent;

public sealed record NoteImageAttachedEvent(NoteId NoteId, Guid ImageId, string OriginalFilename) : DomainEvent;

public sealed record NoteImageDetachedEvent(NoteId NoteId, Guid ImageId) : DomainEvent;

public sealed record NoteDeletedEvent(NoteId NoteId, NoteScopeType ScopeType, string ScopeId) : DomainEvent;
