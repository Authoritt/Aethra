using System.Text.RegularExpressions;
using Aethra.Modules.Services.Domain;
using Aethra.Shared.Contracts.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Aprovisiona usuarios en una instancia MongoDB existente. Cada binding crea un usuario
/// con role <c>readWrite</c> (o <c>read</c> si la binding es ReadOnly) sobre una BD dedicada.
///
/// Mongo no requiere CREATE DATABASE explícito: la primera escritura la materializa, así que
/// nos basta con <c>db.createUser()</c> sobre la BD destino.
/// </summary>
public sealed partial class MongoDbProvisioner : IServiceProvisioner
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifierPattern();

    // MongoDB error codes: https://www.mongodb.com/docs/manual/reference/error-codes/
    private const int MongoDuplicateUserCode = 51003;       // Usuario ya existe

    private readonly IManagedServiceHostResolver _hostResolver;
    private readonly IAdminCredentialsCodec _codec;
    private readonly ILogger<MongoDbProvisioner> _logger;

    public MongoDbProvisioner(
        IManagedServiceHostResolver hostResolver,
        IAdminCredentialsCodec codec,
        ILogger<MongoDbProvisioner> logger)
    {
        _hostResolver = hostResolver;
        _codec = codec;
        _logger = logger;
    }

    public ServiceType SupportedType => ServiceType.MongoDB;

    public async Task<ProvisionOutcome> ProvisionAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        if (!SafeIdentifierPattern().IsMatch(binding.ResourceName) ||
            !SafeIdentifierPattern().IsMatch(newCreds.Username))
        {
            return new ProvisionOutcome(false, "mongodb.invalid_identifier",
                "ResourceName/Username fuera del alfabeto permitido (alphanum + _ + -).");
        }

        var role = binding.Permissions == BindingPermissions.ReadOnly ? "read" : "readWrite";

        var createUserCmd = new BsonDocument
        {
            { "createUser", newCreds.Username },
            { "pwd", newCreds.Password },
            {
                "roles",
                new BsonArray
                {
                    new BsonDocument { { "role", role }, { "db", binding.ResourceName } },
                }
            },
        };

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            var targetDb = client.GetDatabase(binding.ResourceName);
            await targetDb.RunCommandAsync<BsonDocument>(createUserCmd, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new ProvisionOutcome(true, null, null);
        }
        catch (MongoCommandException ex) when (ex.Code == MongoDuplicateUserCode)
        {
            // Idempotente: usuario ya existe. Actualizamos password+role con updateUser.
            return await UpdateUserAsync(service, binding, newCreds, role, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoCommandException ex)
        {
            _logger.LogError(ex, "MongoDB provision falló binding {Binding}: {Code}", binding.Id, ex.Code);
            return new ProvisionOutcome(false, $"mongodb.{ex.Code}", ex.Message);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "MongoDB provision conexión falló binding {Binding}", binding.Id);
            return new ProvisionOutcome(false, "mongodb.connect_failed", ex.Message);
        }
    }

    public async Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);

        if (!SafeIdentifierPattern().IsMatch(binding.ResourceName))
        {
            return new RevokeOutcome(false, "mongodb.invalid_identifier", "ResourceName fuera del alfabeto permitido.");
        }

        // Mismo nombre que el resource sirve como fallback si el caller no nos pasa el user.
        var username = binding.ResourceName;

        var dropUserCmd = new BsonDocument { { "dropUser", username } };

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            var targetDb = client.GetDatabase(binding.ResourceName);
            try
            {
                await targetDb.RunCommandAsync<BsonDocument>(dropUserCmd, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (ex.CodeName == "UserNotFound" || ex.Code == 11)
            {
                // Idempotente: ya no existe.
            }

            // Drop de la BD entera es opcional; lo hacemos en best-effort para limpiar storage.
            try
            {
                await client.DropDatabaseAsync(binding.ResourceName, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoException)
            {
                // Best-effort: si falla, la BD vacía no es bloqueante.
            }

            return new RevokeOutcome(true, null, null);
        }
        catch (MongoCommandException ex)
        {
            _logger.LogError(ex, "MongoDB revoke falló binding {Binding}: {Code}", binding.Id, ex.Code);
            return new RevokeOutcome(false, $"mongodb.{ex.Code}", ex.Message);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "MongoDB revoke conexión falló binding {Binding}", binding.Id);
            return new RevokeOutcome(false, "mongodb.connect_failed", ex.Message);
        }
    }

    public async Task<RotateOutcome> RotateAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        if (!SafeIdentifierPattern().IsMatch(newCreds.Username))
        {
            return new RotateOutcome(false, "mongodb.invalid_identifier", "Username fuera del alfabeto permitido.");
        }

        var role = binding.Permissions == BindingPermissions.ReadOnly ? "read" : "readWrite";
        var updateCmd = new BsonDocument
        {
            { "updateUser", newCreds.Username },
            { "pwd", newCreds.Password },
            {
                "roles",
                new BsonArray
                {
                    new BsonDocument { { "role", role }, { "db", binding.ResourceName } },
                }
            },
        };

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            var targetDb = client.GetDatabase(binding.ResourceName);
            await targetDb.RunCommandAsync<BsonDocument>(updateCmd, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new RotateOutcome(true, null, null);
        }
        catch (MongoCommandException ex)
        {
            _logger.LogError(ex, "MongoDB rotate falló binding {Binding}: {Code}", binding.Id, ex.Code);
            return new RotateOutcome(false, $"mongodb.{ex.Code}", ex.Message);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "MongoDB rotate conexión falló binding {Binding}", binding.Id);
            return new RotateOutcome(false, "mongodb.connect_failed", ex.Message);
        }
    }

    public async Task<TestOutcome> TestConnectionAsync(ManagedService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            var adminDb = client.GetDatabase("admin");
            var ping = await adminDb.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            return new TestOutcome(true, ping.ToJson());
        }
        catch (MongoException ex)
        {
            return new TestOutcome(false, ex.Message);
        }
    }

    private async Task<ProvisionOutcome> UpdateUserAsync(ManagedService service, ServiceBinding binding,
        BindingCredentials newCreds, string role, CancellationToken cancellationToken)
    {
        var updateCmd = new BsonDocument
        {
            { "updateUser", newCreds.Username },
            { "pwd", newCreds.Password },
            {
                "roles",
                new BsonArray
                {
                    new BsonDocument { { "role", role }, { "db", binding.ResourceName } },
                }
            },
        };

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            var targetDb = client.GetDatabase(binding.ResourceName);
            await targetDb.RunCommandAsync<BsonDocument>(updateCmd, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new ProvisionOutcome(true, null, null);
        }
        catch (MongoCommandException ex)
        {
            _logger.LogError(ex, "MongoDB update (idempotent) falló binding {Binding}: {Code}", binding.Id, ex.Code);
            return new ProvisionOutcome(false, $"mongodb.{ex.Code}", ex.Message);
        }
    }

    private async Task<IMongoClient> CreateAdminClientAsync(ManagedService service, CancellationToken cancellationToken)
    {
        var admin = _codec.Decode(service.AdminCredentialsCipher);
        var host = await _hostResolver.ResolveAsync(service, cancellationToken).ConfigureAwait(false);

        var settings = new MongoClientSettings
        {
            Server = new MongoServerAddress(host, service.InternalPort),
            Credential = MongoCredential.CreateCredential("admin", admin.Username, admin.Password),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ServerSelectionTimeout = TimeSpan.FromSeconds(10),
            DirectConnection = true,
        };

        return new MongoClient(settings);
    }
}
