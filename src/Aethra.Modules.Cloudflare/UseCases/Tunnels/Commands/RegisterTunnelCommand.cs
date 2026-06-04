using System.Globalization;
using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;

/// <summary>
/// F13.9 — registra (o re-registra) un Cloudflare Tunnel gestionado remotamente. Verifica el token
/// contra el API real leyendo la config de ingress actual; guarda el token cifrado. Si ya existe un
/// túnel con ese UUID, actualiza token + servicios (idempotente).
/// </summary>
public sealed record RegisterTunnelCommand(
    string AccountId,
    string TunnelId,
    string Name,
    string ApiToken,
    string? AethraService,
    string? FallbackService,
    bool FallbackNoTlsVerify) : ICommand<CloudflareTunnelDto>;

public sealed class RegisterTunnelValidator : AbstractValidator<RegisterTunnelCommand>
{
    public RegisterTunnelValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty().Matches("^[0-9a-fA-F]{32}$")
            .WithMessage("El account_id debe ser hex de 32 caracteres.");
        RuleFor(c => c.TunnelId).NotEmpty().Matches("^[0-9a-fA-F-]{36}$")
            .WithMessage("El tunnel_id debe ser un UUID.");
        RuleFor(c => c.Name).NotEmpty().MaximumLength(128);
        RuleFor(c => c.ApiToken).NotEmpty().MinimumLength(8);
    }
}

internal sealed class RegisterTunnelHandler(
    CloudflareDbContext db,
    ICloudflareApiClient api,
    ICloudflareTokenCodec codec,
    IClock clock) : ICommandHandler<RegisterTunnelCommand, CloudflareTunnelDto>
{
    public async Task<Result<CloudflareTunnelDto>> Handle(RegisterTunnelCommand request, CancellationToken cancellationToken)
    {
        var accountId = request.AccountId.Trim();
        var tunnelId = request.TunnelId.Trim();

        // Verifica el token contra el API real (lee la config remota actual).
        IReadOnlyList<TunnelIngressRule> ingress;
        try
        {
            ingress = await api.GetTunnelIngressAsync(accountId, tunnelId, request.ApiToken, cancellationToken).ConfigureAwait(false);
        }
        catch (CloudflareApiException ex)
        {
            return Error.Validation(
                "cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"Cloudflare rechazó el túnel/token (code {ex.Code}): {ex.Message}"));
        }

        var cipher = codec.Encode(request.ApiToken);
        var now = clock.UtcNow;

        var existing = await db.Tunnels.FirstOrDefaultAsync(t => t.TunnelId == tunnelId, cancellationToken).ConfigureAwait(false);
        CloudflareTunnel tunnel;
        if (existing is not null)
        {
            existing.UpdateToken(cipher, now);
            existing.UpdateServices(request.AethraService, request.FallbackService, request.FallbackNoTlsVerify, now);
            existing.MarkSynced(now);
            tunnel = existing;
        }
        else
        {
            tunnel = CloudflareTunnel.Create(
                tunnelId, request.Name, accountId, cipher,
                request.AethraService, request.FallbackService, request.FallbackNoTlsVerify, now);
            tunnel.MarkSynced(now);
            db.Tunnels.Add(tunnel);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var rules = ingress.Select(r => new TunnelIngressRuleDto(r.Hostname, r.Service, r.NoTlsVerify)).ToList();
        return ToDto(tunnel, rules);
    }

    internal static CloudflareTunnelDto ToDto(CloudflareTunnel t, IReadOnlyList<TunnelIngressRuleDto> ingress)
        => new(t.Id.ToString(), t.TunnelId, t.Name, t.AccountId, t.AethraService, t.FallbackService,
            t.FallbackNoTlsVerify, t.CreatedAt, t.UpdatedAt, t.LastSyncedAt, ingress);
}
