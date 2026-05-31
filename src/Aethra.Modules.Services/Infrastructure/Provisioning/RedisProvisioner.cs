using System.Text.RegularExpressions;
using Aethra.Modules.Services.Domain;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Aprovisiona ACL users sobre Redis 6+. Cada binding obtiene un user con permisos limitados
/// a su <c>ResourceName</c> como prefijo de keys (<c>~prefix:*</c>).
/// </summary>
public sealed partial class RedisProvisioner : IServiceProvisioner
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_:-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeNamePattern();

    private readonly IManagedServiceHostResolver _hostResolver;
    private readonly IAdminCredentialsCodec _codec;
    private readonly ILogger<RedisProvisioner> _logger;

    public RedisProvisioner(
        IManagedServiceHostResolver hostResolver,
        IAdminCredentialsCodec codec,
        ILogger<RedisProvisioner> logger)
    {
        _hostResolver = hostResolver;
        _codec = codec;
        _logger = logger;
    }

    public ServiceType SupportedType => ServiceType.Redis;

    public async Task<ProvisionOutcome> ProvisionAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        if (!SafeNamePattern().IsMatch(newCreds.Username) || !SafeNamePattern().IsMatch(binding.ResourceName))
        {
            return new ProvisionOutcome(false, "redis.invalid_identifier", "Username/ResourceName fuera del alfabeto permitido.");
        }

        var commands = BuildAclSetUser(newCreds.Username, newCreds.Password, binding.ResourceName, binding.Permissions);

        try
        {
            await using var mux = await ConnectAdminAsync(service, cancellationToken).ConfigureAwait(false);
            var db = mux.GetDatabase();
            await db.ExecuteAsync("ACL", commands).ConfigureAwait(false);
            return new ProvisionOutcome(true, null, null);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis provision falló binding {Binding}", binding.Id);
            return new ProvisionOutcome(false, "redis.acl_failed", ex.Message);
        }
    }

    public async Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);

        if (!SafeNamePattern().IsMatch(binding.ResourceName))
        {
            return new RevokeOutcome(false, "redis.invalid_identifier", "ResourceName fuera del alfabeto permitido.");
        }

        try
        {
            await using var mux = await ConnectAdminAsync(service, cancellationToken).ConfigureAwait(false);
            var db = mux.GetDatabase();
            // ACL DELUSER es idempotente: devuelve el conteo de usuarios borrados.
            await db.ExecuteAsync("ACL", "DELUSER", binding.ResourceName).ConfigureAwait(false);
            return new RevokeOutcome(true, null, null);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis revoke falló binding {Binding}", binding.Id);
            return new RevokeOutcome(false, "redis.acl_failed", ex.Message);
        }
    }

    public async Task<RotateOutcome> RotateAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        if (!SafeNamePattern().IsMatch(newCreds.Username))
        {
            return new RotateOutcome(false, "redis.invalid_identifier", "Username fuera del alfabeto permitido.");
        }

        try
        {
            await using var mux = await ConnectAdminAsync(service, cancellationToken).ConfigureAwait(false);
            var db = mux.GetDatabase();
            // resetpass + nuevo password preserva los keyspace patterns y categorías existentes.
            await db.ExecuteAsync(
                "ACL",
                "SETUSER",
                newCreds.Username,
                "resetpass",
                ">" + newCreds.Password).ConfigureAwait(false);
            return new RotateOutcome(true, null, null);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis rotate falló binding {Binding}", binding.Id);
            return new RotateOutcome(false, "redis.acl_failed", ex.Message);
        }
    }

    public async Task<TestOutcome> TestConnectionAsync(ManagedService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            await using var mux = await ConnectAdminAsync(service, cancellationToken).ConfigureAwait(false);
            var db = mux.GetDatabase();
            var pong = await db.PingAsync().ConfigureAwait(false);
            return new TestOutcome(true, pong.ToString());
        }
        catch (RedisException ex)
        {
            return new TestOutcome(false, ex.Message);
        }
    }

    private static object[] BuildAclSetUser(string username, string password, string prefix, BindingPermissions permissions)
    {
        // Forma: SETUSER {user} on >{pwd} ~{prefix}:* +@all -@dangerous
        // ReadOnly recorta a +@read; ReadWrite añade +@write además de @read.
        var permTokens = permissions switch
        {
            BindingPermissions.ReadOnly => new[] { "+@read", "-@write", "-@dangerous" },
            BindingPermissions.ReadWrite => new[] { "+@read", "+@write", "-@dangerous" },
            _ => new[] { "+@all", "-@dangerous" },
        };

        var args = new List<object>(8 + permTokens.Length)
        {
            "SETUSER",
            username,
            "reset",
            "on",
            ">" + password,
            "~" + prefix + ":*",
        };
        args.AddRange(permTokens);
        return [.. args];
    }

    private async Task<IConnectionMultiplexer> ConnectAdminAsync(ManagedService service, CancellationToken cancellationToken)
    {
        var admin = _codec.Decode(service.AdminCredentialsCipher);
        var host = await _hostResolver.ResolveAsync(service, cancellationToken).ConfigureAwait(false);

        var options = new ConfigurationOptions
        {
            EndPoints = { { host, service.InternalPort } },
            User = admin.Username,
            Password = admin.Password,
            AbortOnConnectFail = false,
            ConnectTimeout = 5_000,
            SyncTimeout = 10_000,
        };

        return await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
    }
}
