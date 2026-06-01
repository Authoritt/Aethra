using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aethra.Modules.Services.Domain;
using Aethra.Shared.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Habla con la Management API de RabbitMQ (HTTP). Cada binding equivale a un par
/// <c>vhost + user + permissions</c>.
/// </summary>
public sealed class RabbitProvisioner : IServiceProvisioner
{
    private const string HttpClientName = "aethra-rabbit-admin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IManagedServiceHostResolver _hostResolver;
    private readonly IAdminCredentialsCodec _codec;
    private readonly ILogger<RabbitProvisioner> _logger;

    public RabbitProvisioner(
        IHttpClientFactory httpFactory,
        IManagedServiceHostResolver hostResolver,
        IAdminCredentialsCodec codec,
        ILogger<RabbitProvisioner> logger)
    {
        _httpFactory = httpFactory;
        _hostResolver = hostResolver;
        _codec = codec;
        _logger = logger;
    }

    public ServiceType SupportedType => ServiceType.RabbitMQ;

    public async Task<ProvisionOutcome> ProvisionAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);

            var vhost = Uri.EscapeDataString(binding.ResourceName);
            var user = Uri.EscapeDataString(newCreds.Username);

            await EnsureSuccessAsync(
                await client.PutAsync(new Uri($"api/vhosts/{vhost}", UriKind.Relative), content: null, cancellationToken).ConfigureAwait(false),
                "rabbit.create_vhost").ConfigureAwait(false);

            var userBody = new { password = newCreds.Password, tags = string.Empty };
            await EnsureSuccessAsync(
                await client.PutAsJsonAsync(new Uri($"api/users/{user}", UriKind.Relative), userBody, JsonOptions, cancellationToken).ConfigureAwait(false),
                "rabbit.create_user").ConfigureAwait(false);

            var perms = BuildPermissions(binding.Permissions);
            await EnsureSuccessAsync(
                await client.PutAsJsonAsync(new Uri($"api/permissions/{vhost}/{user}", UriKind.Relative), perms, JsonOptions, cancellationToken).ConfigureAwait(false),
                "rabbit.set_permissions").ConfigureAwait(false);

            return new ProvisionOutcome(true, null, null);
        }
        catch (RabbitOperationException ex)
        {
            _logger.LogError(ex, "Rabbit provision falló binding {Binding}: {Code}", binding.Id, ex.Code);
            return new ProvisionOutcome(false, ex.Code, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Rabbit provision conexión falló binding {Binding}", binding.Id);
            return new ProvisionOutcome(false, "rabbit.http_error", ex.Message);
        }
    }

    public async Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            var vhost = Uri.EscapeDataString(binding.ResourceName);

            // Mismo nombre que el resource sirve como fallback cuando el caller no nos pasa el
            // user explícito; idempotente porque 404 lo aceptamos como éxito.
            var user = Uri.EscapeDataString(binding.ResourceName);

            await EnsureSuccessOrNotFoundAsync(
                await client.DeleteAsync(new Uri($"api/users/{user}", UriKind.Relative), cancellationToken).ConfigureAwait(false),
                "rabbit.delete_user").ConfigureAwait(false);
            await EnsureSuccessOrNotFoundAsync(
                await client.DeleteAsync(new Uri($"api/vhosts/{vhost}", UriKind.Relative), cancellationToken).ConfigureAwait(false),
                "rabbit.delete_vhost").ConfigureAwait(false);

            return new RevokeOutcome(true, null, null);
        }
        catch (RabbitOperationException ex)
        {
            _logger.LogError(ex, "Rabbit revoke falló binding {Binding}: {Code}", binding.Id, ex.Code);
            return new RevokeOutcome(false, ex.Code, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Rabbit revoke conexión falló binding {Binding}", binding.Id);
            return new RevokeOutcome(false, "rabbit.http_error", ex.Message);
        }
    }

    public async Task<RotateOutcome> RotateAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            var user = Uri.EscapeDataString(newCreds.Username);
            var body = new { password = newCreds.Password, tags = string.Empty };

            await EnsureSuccessAsync(
                await client.PutAsJsonAsync(new Uri($"api/users/{user}", UriKind.Relative), body, JsonOptions, cancellationToken).ConfigureAwait(false),
                "rabbit.rotate_user").ConfigureAwait(false);

            return new RotateOutcome(true, null, null);
        }
        catch (RabbitOperationException ex)
        {
            _logger.LogError(ex, "Rabbit rotate falló binding {Binding}: {Code}", binding.Id, ex.Code);
            return new RotateOutcome(false, ex.Code, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Rabbit rotate conexión falló binding {Binding}", binding.Id);
            return new RotateOutcome(false, "rabbit.http_error", ex.Message);
        }
    }

    public async Task<TestOutcome> TestConnectionAsync(ManagedService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            var client = await CreateAdminClientAsync(service, cancellationToken).ConfigureAwait(false);
            using var response = await client.GetAsync(new Uri("api/overview", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new TestOutcome(true, payload.Length > 256 ? payload[..256] : payload);
        }
        catch (HttpRequestException ex)
        {
            return new TestOutcome(false, ex.Message);
        }
    }

    private static PermissionsBody BuildPermissions(BindingPermissions permissions) => permissions switch
    {
        BindingPermissions.ReadOnly => new PermissionsBody(string.Empty, string.Empty, ".*"),
        BindingPermissions.ReadWrite => new PermissionsBody(string.Empty, ".*", ".*"),
        _ => new PermissionsBody(".*", ".*", ".*"),
    };

    private sealed record PermissionsBody(string Configure, string Write, string Read);

    private async Task<HttpClient> CreateAdminClientAsync(ManagedService service, CancellationToken cancellationToken)
    {
        var admin = _codec.Decode(service.AdminCredentialsCipher);
        var host = await _hostResolver.ResolveAsync(service, cancellationToken).ConfigureAwait(false);
        var port = await _hostResolver.ResolveManagementPortAsync(service, cancellationToken).ConfigureAwait(false);

        var client = _httpFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(
            string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}/"),
            UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(15);

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(admin.Username + ":" + admin.Password));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

        return client;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string code)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new RabbitOperationException(code, $"{(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
        }
    }

    private static async Task EnsureSuccessOrNotFoundAsync(HttpResponseMessage response, string code)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }
            var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new RabbitOperationException(code, $"{(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
        }
    }

    private sealed class RabbitOperationException : Exception
    {
        public string Code { get; }
        public RabbitOperationException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
