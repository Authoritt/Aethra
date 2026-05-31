using Aethra.Shared.Kernel.Domain;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// API que los handlers de comando usan para encolar eventos de integracion dentro
/// de su misma transaccion. La implementacion concreta vive en cada modulo y escribe
/// en su propia tabla outbox.
/// </summary>
public interface IOutboxWriter
{
    Task EnqueueAsync(IIntegrationEvent @event, CancellationToken ct);
}
