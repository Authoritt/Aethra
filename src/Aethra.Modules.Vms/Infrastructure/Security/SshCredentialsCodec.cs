using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Vms.Infrastructure.Security;

/// <summary>
/// Tipo de autenticación SSH soportada. <c>Password</c> usa contraseña directa; <c>Key</c>
/// usa una clave privada PEM (RSA, Ed25519, ECDSA).
/// </summary>
public enum SshAuthMethod
{
    Password = 0,
    Key = 1,
}

/// <summary>
/// Credenciales SSH plaintext que el provisioner usa para conectarse a la VM remota.
/// <see cref="Value"/> es la contraseña o el contenido PEM de la key (sin passphrase).
/// </summary>
/// <remarks>
/// Persiste cifrado con DataProtection en <c>Vm.SshCredentialsCipher</c>. El plaintext SOLO
/// existe en memoria mientras el provisioner las usa.
/// </remarks>
public sealed record SshCredentials(string Host, int Port, string User, SshAuthMethod AuthMethod, string Value)
{
    public const int MaxValueLength = 16 * 1024;

    /// <summary>Valida los campos. Devuelve null si OK o el código de error.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return "ssh.host_required";
        }
        if (Port <= 0 || Port > 65535)
        {
            return "ssh.port_invalid";
        }
        if (string.IsNullOrWhiteSpace(User))
        {
            return "ssh.user_required";
        }
        if (string.IsNullOrEmpty(Value))
        {
            return "ssh.value_required";
        }
        if (Value.Length > MaxValueLength)
        {
            return "ssh.value_too_large";
        }
        return null;
    }
}

/// <summary>
/// Codec de credenciales SSH. Análogo a <c>DataProtectionWebhookSecretCodec</c>:
/// usa <see cref="IDataProtector"/> con purpose <c>aethra-vm-ssh-creds</c> para cifrar/descifrar
/// el JSON con las credenciales. Si las DataProtection keys se pierden, las credenciales
/// quedan ilegibles y hay que re-tipear.
/// </summary>
public interface ISshCredentialsCodec
{
    /// <summary>Cifra las credenciales en bytes listos para persistir en bytea.</summary>
    byte[] Encode(SshCredentials creds);
    /// <summary>Descifra el cipher de la BD a credenciales en memoria.</summary>
    SshCredentials Decode(byte[] cipher);
}

public sealed class DataProtectionSshCredentialsCodec : ISshCredentialsCodec
{
    public const string Purpose = "aethra-vm-ssh-creds";

    private readonly IDataProtector _protector;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DataProtectionSshCredentialsCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Encode(SshCredentials creds)
    {
        ArgumentNullException.ThrowIfNull(creds);
        var validation = creds.Validate();
        if (validation is not null)
        {
            throw new ArgumentException($"Credenciales SSH inválidas: {validation}", nameof(creds));
        }
        var json = JsonSerializer.Serialize(creds, JsonOptions);
        return _protector.Protect(Encoding.UTF8.GetBytes(json));
    }

    public SshCredentials Decode(byte[] cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        if (cipher.Length == 0)
        {
            throw new ArgumentException("Cipher vacío.", nameof(cipher));
        }
        var json = Encoding.UTF8.GetString(_protector.Unprotect(cipher));
        return JsonSerializer.Deserialize<SshCredentials>(json, JsonOptions)
            ?? throw new InvalidOperationException("No se pudo deserializar las credenciales SSH.");
    }
}
