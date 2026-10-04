using System.Diagnostics;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Sonda, UNA sola vez por proceso, si hay un runtime de contenedores utilizable.
/// </summary>
/// <remarks>
/// No basta con que el binario exista: en esta maquina el 2026-09-18 `podman` estaba instalado y la
/// distro WSL creada, pero la conexion nunca se registro, asi que cualquier comando fallaba. Por eso
/// la sonda ejecuta `podman version` y mira el codigo de salida, no la presencia del ejecutable.
/// </remarks>
internal static class ContainerRuntime
{
    private static readonly Lazy<string?> Probe = new(() =>
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "podman",
                    ArgumentList = { "version", "--format", "{{.Server.Version}}" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.Start();
            var stderr = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                return "`podman version` no respondio en 20 s";
            }

            return process.ExitCode == 0
                ? null
                : "`podman version` salio con codigo " + process.ExitCode + ": " + stderr.Trim();
        }
        catch (Exception ex)
        {
            return "no se pudo ejecutar `podman`: " + ex.Message;
        }
    }, isThreadSafe: true);

    /// <summary><c>null</c> si hay runtime; si no, el motivo exacto, para que el salto lo explique.</summary>
    internal static string? UnavailableReason => Probe.Value;
}

/// <summary>
/// <see cref="TheoryAttribute"/> que se marca SALTADO —no fallido— cuando no hay runtime de
/// contenedores.
/// </summary>
/// <remarks>
/// El motivo de existir: acoplar `dotnet test` a que la maquina tenga un runtime rompe la suite
/// entera para todo el que no lo tenga, y este runtime ya se demostro fragil. Pero saltar NO puede
/// ser un verde silencioso: xUnit cuenta los saltados aparte, el motivo va escrito, y el piso de
/// verificacion de la OT declara cuantos saltados son aceptables. Un saltado que nadie cuenta si
/// seria un gate que no bloquea.
/// </remarks>
internal sealed class RequiresContainerRuntimeTheoryAttribute : TheoryAttribute
{
    public RequiresContainerRuntimeTheoryAttribute()
    {
        var reason = ContainerRuntime.UnavailableReason;
        if (reason is not null)
        {
            Skip = "Requiere un runtime de contenedores para levantar redis:7-alpine — " + reason;
        }
    }
}
