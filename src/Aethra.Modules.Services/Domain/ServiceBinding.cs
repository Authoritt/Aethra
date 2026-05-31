using Aethra.Modules.Services.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Services.Domain;

/// <summary>
/// Relación "Instance X usa servicio Y". Cada binding tiene su propio recurso aislado
/// (BD/vhost/prefix) y credenciales independientes. Al crearse, el provisioner crea
/// los recursos y se inyectan env vars/secrets en la Instance.
///
/// Las credenciales se cifran con DataProtection (purpose: "aethra-binding-creds").
/// </summary>
public sealed class ServiceBinding : AggregateRoot<ServiceBindingId>
{
    public ManagedServiceId ServiceId { get; private set; }
    public string InstanceId { get; private set; }
    public string ResourceName { get; private set; }
    public byte[] CredentialsCipher { get; private set; }
    public BindingPermissions Permissions { get; private set; }
    public string InjectedEnvVarPrefix { get; private set; }
    public MigrationsHook? MigrationsHook { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProvisionedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastRotatedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    private ServiceBinding(ServiceBindingId id, ManagedServiceId serviceId, string instanceId,
        string resourceName, byte[] credentialsCipher, BindingPermissions permissions,
        string injectedEnvVarPrefix, MigrationsHook? hook, DateTimeOffset now) : base(id)
    {
        ServiceId = serviceId;
        InstanceId = instanceId;
        ResourceName = resourceName;
        CredentialsCipher = credentialsCipher;
        Permissions = permissions;
        InjectedEnvVarPrefix = injectedEnvVarPrefix;
        MigrationsHook = hook;
        CreatedAt = now;
    }

    public static ServiceBinding Create(ManagedServiceId serviceId, string instanceId, string resourceName,
        byte[] credentialsCipher, BindingPermissions permissions, string? injectedEnvVarPrefix,
        MigrationsHook? hook, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("InstanceId requerido.", nameof(instanceId));
        }
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("ResourceName requerido.", nameof(resourceName));
        }
        if (credentialsCipher is null || credentialsCipher.Length == 0)
        {
            throw new ArgumentException("Credenciales cifradas requeridas.", nameof(credentialsCipher));
        }

        var prefix = (injectedEnvVarPrefix ?? string.Empty).Trim().ToUpperInvariant();
        if (prefix.Length > 0 && !prefix.EndsWith('_'))
        {
            prefix += "_";
        }

        var binding = new ServiceBinding(ServiceBindingId.New(), serviceId, instanceId, resourceName.Trim(),
            credentialsCipher, permissions, prefix, hook, now);
        binding.Raise(new ServiceBindingCreatedEvent(binding.Id, serviceId, instanceId, binding.ResourceName));
        return binding;
    }

    public void MarkProvisioned(DateTimeOffset now)
    {
        if (ProvisionedAt is not null) { return; }
        ProvisionedAt = now;
        Raise(new ServiceBindingProvisionedEvent(Id, InstanceId, ResourceName, InjectedEnvVarPrefix));
    }

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt is not null) { return; }
        RevokedAt = now;
        Raise(new ServiceBindingRevokedEvent(Id, InstanceId));
    }

    public void RotateCredentials(byte[] newCipher, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("No se pueden rotar credenciales de un binding revocado.");
        }
        CredentialsCipher = newCipher;
        LastRotatedAt = now;
        Raise(new ServiceBindingCredentialsRotatedEvent(Id, InstanceId));
    }

    public void SetMigrationsHook(MigrationsHook? hook)
    {
        MigrationsHook = hook;
    }

    // EF Core
    private ServiceBinding() : base()
    {
        InstanceId = string.Empty;
        ResourceName = string.Empty;
        CredentialsCipher = [];
        InjectedEnvVarPrefix = string.Empty;
    }
}
