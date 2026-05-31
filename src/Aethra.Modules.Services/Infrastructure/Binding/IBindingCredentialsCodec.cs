using System.Text.Json;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Services.Infrastructure.Binding;

/// <summary>
/// Cifra/descifra <see cref="BindingCredentials"/> (user+password generados por binding)
/// con DataProtection purpose <c>aethra-binding-creds</c> — separado del admin codec.
/// </summary>
public interface IBindingCredentialsCodec
{
    byte[] Encode(BindingCredentials credentials);
    BindingCredentials Decode(byte[] cipher);
}

public sealed class DataProtectionBindingCredentialsCodec : IBindingCredentialsCodec
{
    private const string Purpose = "aethra-binding-creds";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDataProtector _protector;

    public DataProtectionBindingCredentialsCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(BindingCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        return _protector.Protect(bytes);
    }

    public BindingCredentials Decode(byte[] cipher)
    {
        if (cipher is null || cipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacío.", nameof(cipher));
        }
        var json = _protector.Unprotect(cipher);
        var creds = JsonSerializer.Deserialize<BindingCredentials>(json, JsonOptions)
            ?? throw new InvalidOperationException("BindingCredentials inválidas tras Unprotect.");
        return creds;
    }
}
