using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Vms.UseCases.Vms.Commands;

/// <summary>
/// Registra una nueva VM y emite el token de satélite inicial.
/// La respuesta incluye el token plaintext UNA SOLA VEZ.
/// </summary>
public sealed record RegisterVmCommand(
    string Name,
    string? Slug = null,
    string? PublicIp = null,
    string? PrivateIp = null,
    string? Description = null) : ICommand<RegisterVmResult>;

public sealed record RegisterVmResult(
    string VmId,
    string Slug,
    string Name,
    string TokenPlaintext,
    string InstallScript);

public sealed class RegisterVmValidator : AbstractValidator<RegisterVmCommand>
{
    public RegisterVmValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(255);
    }
}

internal sealed class RegisterVmHandler(VmsDbContext db, IClock clock)
    : ICommandHandler<RegisterVmCommand, RegisterVmResult>
{
    public async Task<Result<RegisterVmResult>> Handle(RegisterVmCommand request, CancellationToken cancellationToken)
    {
        var slugResult = request.Slug is { Length: > 0 }
            ? Slug.Create(request.Slug)
            : Slug.Suggest(request.Name);
        if (slugResult.IsFailure)
        {
            return slugResult.Error;
        }
        var slug = slugResult.Value;

        if (await db.Vms.AnyAsync(v => v.Slug == slug, cancellationToken))
        {
            return Error.Conflict("vm.slug_taken", $"Ya existe una VM con slug '{slug}'.");
        }

        var (token, vm) = Vm.Register(slug, request.Name, clock.UtcNow,
            publicIp: request.PublicIp, privateIp: request.PrivateIp, description: request.Description);

        db.Vms.Add(vm);
        await db.SaveChangesAsync(cancellationToken);

        return new RegisterVmResult(
            VmId: vm.Id.ToString(),
            Slug: slug.Value,
            Name: vm.Name,
            TokenPlaintext: token,
            InstallScript: BuildInstallScript(vm, token));
    }

    private static string BuildInstallScript(Vm vm, string token)
    {
        // Script bash para Linux. El usuario lo pega en la VM y deja corriendo el satélite.
        // En F3+ generaremos el binario AOT del satélite y este script lo descargará.
        // Usamos raw-string sin interpolación y reemplazamos placeholders con String.Replace.
        // Esto evita conflictos entre la interpolación C# y los `${VAR}` de bash.
        const string template = """
            #!/usr/bin/env bash
            # Aethra satellite installer — VM: __VM_NAME__ (__VM_SLUG__)
            set -euo pipefail
            export AETHRA_CENTRAL_URL="${AETHRA_CENTRAL_URL:-https://aethra.tu-dominio.com}"
            export AETHRA_SATELLITE_TOKEN="__TOKEN__"
            export AETHRA_VM_SLUG="__VM_SLUG__"
            echo "Token guardado en /etc/aethra/satellite.env (modo 600)."
            sudo mkdir -p /etc/aethra
            cat <<EOF | sudo tee /etc/aethra/satellite.env >/dev/null
            AETHRA_CENTRAL_URL=$AETHRA_CENTRAL_URL
            AETHRA_SATELLITE_TOKEN=$AETHRA_SATELLITE_TOKEN
            AETHRA_VM_SLUG=$AETHRA_VM_SLUG
            EOF
            sudo chmod 600 /etc/aethra/satellite.env
            echo "Listo. En F3+ este script descargará y arrancará el binario systemd."
            """;
        return template
            .Replace("__VM_NAME__", vm.Name)
            .Replace("__VM_SLUG__", vm.Slug.Value)
            .Replace("__TOKEN__", token);
    }
}
