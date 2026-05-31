using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain.Events;

public sealed record DeployJobQueuedEvent(DeployJobId JobId, string ApplicationId, string GitSha) : DomainEvent;

public sealed record DeployStatusChangedDomainEvent(DeployJobId JobId, DeployStatus From, DeployStatus To)
    : DomainEvent;

public sealed record DeployJobCompletedDomainEvent(DeployJobId JobId, string ApplicationId, string ContainerName,
    int Port) : DomainEvent;

public sealed record DeployJobFailedDomainEvent(DeployJobId JobId, string ApplicationId, DeployStatus FailedAtStage,
    string ErrorCode, string ErrorMessage) : DomainEvent;
