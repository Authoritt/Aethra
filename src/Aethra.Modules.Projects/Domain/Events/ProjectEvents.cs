using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Events;

public sealed record ProjectCreatedEvent(ProjectId ProjectId, string Slug, string Name) : DomainEvent;

public sealed record ProjectRenamedEvent(ProjectId ProjectId, string OldName, string NewName) : DomainEvent;

public sealed record ProjectDeletedEvent(ProjectId ProjectId) : DomainEvent;

public sealed record EnvironmentAddedEvent(ProjectId ProjectId, EnvironmentId EnvironmentId, string Name) : DomainEvent;

public sealed record EnvironmentRemovedEvent(ProjectId ProjectId, EnvironmentId EnvironmentId) : DomainEvent;

public sealed record ApplicationCreatedEvent(
    ProjectId ProjectId,
    EnvironmentId EnvironmentId,
    ApplicationId ApplicationId,
    string Slug,
    string Name,
    string GitRepoUrl,
    string Branch
) : DomainEvent;

public sealed record ApplicationRenamedEvent(ApplicationId ApplicationId, string OldName, string NewName) : DomainEvent;

public sealed record ApplicationDeletedEvent(ApplicationId ApplicationId) : DomainEvent;
