using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Vms.Infrastructure.Provisioning;

/// <summary>
/// <see cref="BackgroundService"/> que drena <see cref="IInstallationJobQueue"/> en orden FIFO.
/// Procesa uno a la vez (no paralelo) para mantener determinismo. Para cada job:
/// <list type="bullet">
/// <item>Marca <c>Vm.BeginInstall</c> + persiste.</item>
/// <item>Crea un IProgress que (a) emite SignalR via <see cref="IInstallProgressNotifier"/>
/// y (b) appendea al log persistido en BD periódicamente.</item>
/// <item>Llama al <see cref="ISshProvisioner"/>.</item>
/// <item>Marca <c>MarkInstalled</c> / <c>MarkInstallFailed</c> y persiste.</item>
/// </list>
/// Crea su propio scope por job para tener un DbContext fresco.
/// </summary>
public sealed class InstallationDispatcher(
    IInstallationJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<InstallationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("InstallationDispatcher arrancando");
        await foreach (var job in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            using var scope = scopeFactory.CreateScope();
            try
            {
                await RunOneAsync(job, scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error procesando install para VM {VmId}", job.VmId);
            }
        }
    }

    private async Task RunOneAsync(InstallationJob job, IServiceProvider services, CancellationToken ct)
    {
        var clock = services.GetRequiredService<IClock>();
        var db = services.GetRequiredService<VmsDbContext>();
        var notifier = services.GetRequiredService<IInstallProgressNotifier>();
        var provisioner = services.GetRequiredService<ISshProvisioner>();

        // Cargar VM
        if (!AethraId.TryParse(job.VmId, out var parsed) || parsed.Value.Prefix != "vm")
        {
            logger.LogError("VmId inválido en job de install: {VmId}", job.VmId);
            return;
        }
        var vmId = new VmId(parsed.Value);
        var vm = await db.Vms.FirstOrDefaultAsync(v => v.Id == vmId, ct);
        if (vm is null)
        {
            logger.LogError("VM no encontrada en la BD: {VmId}", job.VmId);
            return;
        }

        vm.BeginInstall(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await notifier.PublishStatusAsync(job.VmId, "Installing", cancellationToken: ct);

        // Buffer en memoria de las últimas líneas para append a BD al final.
        var lines = new List<string>(capacity: 256);
        // Progress emite a SignalR y bufferea para BD.
        var progress = new Progress<string>(line =>
        {
            try
            {
                lines.Add(line);
                _ = notifier.PublishLogAsync(job.VmId, line, "info", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error emitiendo log de install");
            }
        });

        // Timeout total 10 min para todo el flow.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

        InstallResult result;
        try
        {
            result = await provisioner.InstallSatelliteAsync(job.VmId, job.Credentials, job.Options,
                progress, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            result = new InstallResult(false, "install_timeout", "Install excedió el timeout de 10 minutos.",
                string.Empty);
            lines.Add("[timeout] Install excedió 10 minutos.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioner falló para VM {VmId}", job.VmId);
            result = new InstallResult(false, "provisioner_exception", ex.Message, string.Empty);
            lines.Add($"[exception] {ex.Message}");
        }

        // Persistir log + status final.
        var now = clock.UtcNow;
        // Refrescamos la entity para evitar staleness si algo cambió mientras corría.
        await db.Entry(vm).ReloadAsync(ct);
        foreach (var line in lines)
        {
            vm.AppendInstallLog(line);
        }
        if (result.Success)
        {
            vm.MarkInstalled(now);
            await notifier.PublishStatusAsync(job.VmId, "Installed", cancellationToken: ct);
        }
        else
        {
            vm.MarkInstallFailed(result.ErrorCode ?? "unknown", result.ErrorMessage ?? "—", now);
            await notifier.PublishStatusAsync(job.VmId, "Failed", result.ErrorCode, cancellationToken: ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
