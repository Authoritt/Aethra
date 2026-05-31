namespace Aethra.Modules.Services.Domain;

/// <summary>
/// Hook opcional ejecutado vía <c>docker exec</c> dentro del contenedor recién desplegado,
/// después del healthcheck y antes del swap atómico de proxy. Si falla con
/// <c>FailDeployOnError=true</c>, el deploy revierte (el contenedor viejo sigue sirviendo tráfico).
/// </summary>
public sealed record MigrationsHook(
    string Command,
    int TimeoutSeconds,
    bool FailDeployOnError,
    MigrationsHookRunOn RunOn);

public enum MigrationsHookRunOn
{
    EachDeploy,
    FirstDeployOnly,
    ManualTrigger,
}
