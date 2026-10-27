using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

public enum BootstrapAuthenticationOutcome
{
    UsePersistedUsers,
    Disabled,
    InvalidCredentials,
    Succeeded,
}

/// <summary>
/// Decide si una peticion puede usar la credencial transitoria de bootstrap.
/// La existencia de cualquier usuario persistido tiene precedencia sobre la bandera: una vez
/// aprovisionada la instalacion, esta credencial no vuelve a participar en el login normal.
/// </summary>
public sealed class BootstrapAuthenticationService(
    SingleUserStore singleUserStore,
    IAuthenticationAttemptLimiter attemptLimiter,
    IOptions<IdentityOptions> options,
    ILogger<BootstrapAuthenticationService> logger,
    IHostEnvironment? environment = null)
{
    public bool IsBootstrapEnabled =>
        options.Value.AllowBootstrapLogin || (environment?.IsDevelopment() == true);

    public async ValueTask<BootstrapAuthenticationOutcome> AuthenticateAsync(
        bool hasPersistedUsers,
        string? email,
        string? password,
        CancellationToken cancellationToken)
    {
        if (hasPersistedUsers)
        {
            return BootstrapAuthenticationOutcome.UsePersistedUsers;
        }

        var enabled = IsBootstrapEnabled;
        logger.LogWarning(
            new EventId(2100, "BootstrapLoginAttempted"),
            "Intento de autenticacion por bootstrap con tabla de usuarios vacia; habilitado={BootstrapEnabled}",
            enabled);

        if (!enabled)
        {
            logger.LogWarning(
                new EventId(2101, "BootstrapLoginRejected"),
                "Autenticacion por bootstrap rechazada porque Identity:AllowBootstrapLogin esta deshabilitado");
            return BootstrapAuthenticationOutcome.Disabled;
        }

        if (password is null || !await singleUserStore.ValidateCredentialsAsync(
                email,
                password,
                attemptLimiter,
                cancellationToken))
        {
            logger.LogWarning(
                new EventId(2102, "BootstrapLoginInvalidCredentials"),
                "Autenticacion por bootstrap rechazada por credenciales invalidas");
            return BootstrapAuthenticationOutcome.InvalidCredentials;
        }

        logger.LogWarning(
            new EventId(2103, "BootstrapLoginSucceeded"),
            "Autenticacion por bootstrap concedida; debe crearse un usuario persistido y deshabilitarse Identity:AllowBootstrapLogin");
        return BootstrapAuthenticationOutcome.Succeeded;
    }
}
