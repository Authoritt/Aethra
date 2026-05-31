using Aethra.Modules.Services.Domain;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Adaptador por <see cref="ServiceType"/>. Cada implementación habla con la instancia real
/// (Postgres/Redis/RabbitMQ) para crear/revocar/rotar recursos por binding.
/// </summary>
public interface IServiceProvisioner
{
    ServiceType SupportedType { get; }

    Task<ProvisionOutcome> ProvisionAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken);

    Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, CancellationToken cancellationToken);

    Task<RotateOutcome> RotateAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken);

    Task<TestOutcome> TestConnectionAsync(ManagedService service, CancellationToken cancellationToken);
}

public sealed record BindingCredentials(string Username, string Password);

public sealed record ProvisionOutcome(bool Success, string? ErrorCode, string? ErrorMessage);

public sealed record RevokeOutcome(bool Success, string? ErrorCode, string? ErrorMessage);

public sealed record RotateOutcome(bool Success, string? ErrorCode, string? ErrorMessage);

public sealed record TestOutcome(bool Success, string? Detail);
