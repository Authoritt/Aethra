using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Services.Domain.Events;

public sealed record ManagedServiceCreatedEvent(ManagedServiceId ServiceId, ServiceType Type, string Slug, string TargetVmId)
    : DomainEvent;

public sealed record ManagedServiceProvisionedEvent(ManagedServiceId ServiceId, string Slug)
    : DomainEvent;

public sealed record ManagedServiceFailedEvent(ManagedServiceId ServiceId, string Slug, string ErrorCode, string ErrorMessage)
    : DomainEvent;

public sealed record ServiceBindingCreatedEvent(ServiceBindingId BindingId, ManagedServiceId ServiceId, string InstanceId, string ResourceName)
    : DomainEvent;

public sealed record ServiceBindingProvisionedEvent(ServiceBindingId BindingId, string InstanceId, string ResourceName, string EnvVarPrefix)
    : DomainEvent;

public sealed record ServiceBindingRevokedEvent(ServiceBindingId BindingId, string InstanceId)
    : DomainEvent;

public sealed record ServiceBindingCredentialsRotatedEvent(ServiceBindingId BindingId, string InstanceId)
    : DomainEvent;
