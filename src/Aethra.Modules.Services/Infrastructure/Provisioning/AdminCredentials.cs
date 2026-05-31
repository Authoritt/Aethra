using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Credenciales admin de la instancia de servicio (no del binding). Las creamos al provisionar
/// la <c>ManagedService</c> y las almacenamos cifradas en <c>AdminCredentialsCipher</c>.
/// </summary>
public sealed record AdminCredentials(string Username, string Password);

public interface IAdminCredentialsCodec
{
    byte[] Encode(AdminCredentials credentials);

    AdminCredentials Decode(byte[] cipher);
}

public sealed class DataProtectionAdminCredentialsCodec : IAdminCredentialsCodec
{
    // Purpose separado del binding-creds: comprometer uno no expone el otro.
    private const string Purpose = "aethra-svc-admin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDataProtector _protector;

    public DataProtectionAdminCredentialsCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(AdminCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        return _protector.Protect(json);
    }

    public AdminCredentials Decode(byte[] cipher)
    {
        if (cipher is null || cipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacío.", nameof(cipher));
        }
        var json = _protector.Unprotect(cipher);
        var creds = JsonSerializer.Deserialize<AdminCredentials>(json, JsonOptions)
            ?? throw new InvalidOperationException("AdminCredentials JSON inválido tras Unprotect.");
        if (string.IsNullOrWhiteSpace(creds.Username) || string.IsNullOrWhiteSpace(creds.Password))
        {
            throw new InvalidOperationException("AdminCredentials con campos vacíos.");
        }
        return creds;
    }
}
