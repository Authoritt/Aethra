using Aethra.Modules.Notes.Domain;
using Aethra.Modules.Notes.Infrastructure;
using Aethra.Modules.Notes.Infrastructure.Images;
using Aethra.Modules.Notes.Infrastructure.Security;
using Aethra.Modules.Notes.Presentation;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Notes;

/// <summary>
/// Punto de entrada del módulo Notes.
///
/// Wiring desde apps/api/Program.cs:
/// - <see cref="AddNotesModule"/> en builder.Services para DI (DbContext + image store + codec).
/// - <see cref="MapNotesModuleEndpoints"/> en app después de UseAuthorization() para rutas REST.
/// </summary>
public static class NotesModule
{
    public static IServiceCollection AddNotesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<NotesDbContext>(conn);

        // Store local de imágenes (config: Notes:ImageDir). En producción se monta un volumen
        // persistente sobre esa ruta.
        services.AddSingleton<INoteImageStore, LocalNoteImageStore>();

        // Codec de pinned facts: usa DataProtection con purpose "aethra-pinned-facts".
        services.AddSingleton<IPinnedFactCodec, DataProtectionPinnedFactCodec>();

        return services;
    }

    public static IEndpointRouteBuilder MapNotesModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapNotesEndpoints();
}
