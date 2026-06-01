using Aethra.Shared.Infrastructure.Persistence;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// API que los handlers de comando usan para encolar eventos de integracion dentro
/// de su misma transaccion. La implementacion concreta vive en cada modulo y escribe
/// en su propia tabla outbox.
///
/// **Importante**: el genérico <typeparamref name="TDbContext"/> garantiza que cada handler
/// recibe el writer ligado al DbContext de SU MISMO scope. Si fuese una interface no-generic,
/// el último módulo registrado en DI ganaría y todos los handlers escribirían contra ese
/// DbContext — los <c>Add</c> nunca quedarían en la transacción del handler real y
/// <c>SaveChanges</c> no persistiría los outbox rows.
/// </summary>
public interface IOutboxWriter<TDbContext> where TDbContext : AethraModuleDbContext
{
    Task EnqueueAsync(IIntegrationEvent @event, CancellationToken ct);
}
