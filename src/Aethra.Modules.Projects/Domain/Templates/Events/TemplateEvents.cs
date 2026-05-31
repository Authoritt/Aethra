using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Templates.Events;

/// <summary>
/// Disparado cuando se crea un <see cref="Template"/> dentro de un Project.
/// Lo consume el módulo Deployments para suscribirse a webhooks del repo.
/// </summary>
public sealed record TemplateCreatedEvent(
    TemplateId TemplateId,
    ProjectId ProjectId,
    string Slug,
    string GitRepoUrl,
    string Branch) : DomainEvent;

/// <summary>
/// Disparado cuando cambia el origen Git (URL, branch, base directory, watch paths).
/// El módulo Deployments resincroniza la suscripción a webhooks si la URL cambió.
/// </summary>
public sealed record TemplateSourceUpdatedEvent(
    TemplateId TemplateId,
    string GitRepoUrl,
    string Branch) : DomainEvent;

/// <summary>
/// Disparado cuando se rota el webhook secret. El consumidor debe invalidar firmas previas.
/// </summary>
public sealed record TemplateWebhookRotatedEvent(TemplateId TemplateId) : DomainEvent;
