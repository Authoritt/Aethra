using Aethra.Modules.Services.Domain;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// MariaDB y MySQL comparten wire protocol y casi toda la sintaxis DDL.
/// Esta clase es un alias del <see cref="MySqlProvisioner"/> con un <see cref="SupportedType"/>
/// distinto para que el dispatch del registry resuelva la implementación correcta.
/// </summary>
public sealed class MariaDbProvisioner : MySqlProvisioner
{
    public MariaDbProvisioner(
        IManagedServiceHostResolver hostResolver,
        IAdminCredentialsCodec codec,
        ILogger<MariaDbProvisioner> logger)
        : base(hostResolver, codec, logger)
    {
    }

    public override ServiceType SupportedType => ServiceType.MariaDB;
}
