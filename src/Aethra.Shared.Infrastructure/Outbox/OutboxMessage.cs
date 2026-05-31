namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Mensaje persistido en la tabla outbox del modulo emisor.
/// El dispatcher lo lee, lo deserializa al tipo TYPE indicado y lo publica al bus in-memory.
///
/// Convencion: cada modulo tiene su propia tabla outbox_messages dentro de su schema EF
/// (ej. <c>projects.outbox_messages</c>, <c>deployments.outbox_messages</c>).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;          // FQN del IIntegrationEvent
    public string Payload { get; set; } = default!;       // JSON serializado
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    public bool IsPending => ProcessedAt is null;
}
