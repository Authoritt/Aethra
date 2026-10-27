using System.Security.Cryptography;
using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure.Provisioning;
using Aethra.Modules.Vms.Infrastructure.Security;
using Aethra.Shared.Contracts.Containers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Common;
using Xunit;
using ISshClient = Aethra.Modules.Vms.Infrastructure.Provisioning.ISshClient;
using ISshCommand = Aethra.Modules.Vms.Infrastructure.Provisioning.ISshCommand;

namespace Aethra.Modules.Vms.Tests;

/// <summary>
/// Pruebas de regresión completas para el issue #33 (verificación de huellas SSH durante el aprovisionamiento).
/// Cubre:
/// 1. Clave de host no confiable rechazada silenciosamente (password y private-key).
/// 2. Clave fijada/esperada debe coincidir antes de enviar credenciales o comandos (password y private-key).
/// 3. Clave cambiada produce error específico de seguridad con alerta de MITM.
/// 4. Flujo de rotación explícita por el operador.
/// 5. Sondeo previo de clave de host (probe) sin enviar credenciales.
/// Todas las pruebas se ejecutan sin red real abstrayendo el cliente SSH.
/// </summary>
public sealed class SshHostKeyProvisioningTests
{
    private static readonly byte[] ServerKeyBytesA = [
        0x00, 0x00, 0x00, 0x0b, 0x73, 0x73, 0x68, 0x2d, 0x65, 0x64, 0x32, 0x35, 0x35, 0x31, 0x39,
        0x00, 0x00, 0x00, 0x20, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x01, 0x02, 0x03, 0x04, 0x05,
        0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14,
        0x15, 0x16, 0x17, 0x18, 0x19, 0x1a
    ];

    private static readonly byte[] ServerKeyBytesB = [
        0x00, 0x00, 0x00, 0x0b, 0x73, 0x73, 0x68, 0x2d, 0x65, 0x64, 0x32, 0x35, 0x35, 0x31, 0x39,
        0x00, 0x00, 0x00, 0x20, 0x99, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x00, 0x05,
        0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14,
        0x15, 0x16, 0x17, 0x18, 0x19, 0x1a
    ];

    private static readonly string FingerprintA = SshHostKeyHelper.FormatFingerprint(ServerKeyBytesA);
    private static readonly string FingerprintB = SshHostKeyHelper.FormatFingerprint(ServerKeyBytesB);

    private static readonly string TestRsaPrivateKeyPem = RSA.Create(2048).ExportRSAPrivateKeyPem();

    private readonly FakeSatelliteConnectionRegistry _registry = new();

    private SshCredentials CreatePasswordCredentials() => new(
        Host: "192.168.1.100",
        Port: 22,
        User: "ubuntu",
        AuthMethod: SshAuthMethod.Password,
        Value: "SecretPassword123!");

    private SshCredentials CreatePrivateKeyCredentials() => new(
        Host: "192.168.1.100",
        Port: 22,
        User: "ubuntu",
        AuthMethod: SshAuthMethod.Key,
        Value: TestRsaPrivateKeyPem);

    private InstallOptions CreateInstallOptions(string? expectedFingerprint) => new(
        CentralUrl: "http://central.aethra.internal",
        TokenPlaintext: "sat_token_test_123",
        ContainerRuntime: "docker",
        InstallContainerRuntime: false,
        ExpectedHostKeyFingerprint: expectedFingerprint);

    // =========================================================================
    // CRITERIO 1: No conectar a hosts SSH no confiables silenciosamente
    // =========================================================================

    [Fact]
    public async Task UntrustedHost_with_password_auth_rejects_loudly_without_sending_credentials_or_commands()
    {
        var fakeClient = new FakeSshClient(ServerKeyBytesA, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClient);
        var provisioner = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactory);

        // Sin huella esperada
        var options = CreateInstallOptions(expectedFingerprint: null);
        var creds = CreatePasswordCredentials();

        var result = await provisioner.InstallSatelliteAsync(
            "vm_123", creds, options, new Progress<string>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ssh_host_key_untrusted");
        result.ErrorMessage.Should().Contain("Conexión rechazada por seguridad");

        // FENCE DE SEGURIDAD: Nunca se debieron ejecutar comandos ni confiar en la conexión
        fakeClient.ExecutedCommands.Should().BeEmpty();
        fakeClient.CredentialsTransmitted.Should().BeFalse();
        fakeClient.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task UntrustedHost_with_private_key_auth_rejects_loudly_without_sending_credentials_or_commands()
    {
        var fakeClient = new FakeSshClient(ServerKeyBytesA, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClient);
        var provisioner = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactory);

        // Sin huella esperada
        var options = CreateInstallOptions(expectedFingerprint: null);
        var creds = CreatePrivateKeyCredentials();

        var result = await provisioner.InstallSatelliteAsync(
            "vm_123", creds, options, new Progress<string>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ssh_host_key_untrusted");
        result.ErrorMessage.Should().Contain("Conexión rechazada por seguridad");

        // FENCE DE SEGURIDAD
        fakeClient.ExecutedCommands.Should().BeEmpty();
        fakeClient.CredentialsTransmitted.Should().BeFalse();
        fakeClient.IsConnected.Should().BeFalse();
    }

    // =========================================================================
    // CRITERIO 2: La clave fijada/esperada debe coincidir antes de autenticar
    // =========================================================================

    [Fact]
    public async Task MatchingHostKey_with_password_auth_proceeds_and_completes_installation()
    {
        var fakeClient = new FakeSshClient(ServerKeyBytesA, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClient);
        var provisioner = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactory);

        var options = CreateInstallOptions(expectedFingerprint: FingerprintA);
        var creds = CreatePasswordCredentials();

        _registry.AutoConnectOnVm = "vm_123";

        var result = await provisioner.InstallSatelliteAsync(
            "vm_123", creds, options, new Progress<string>(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.VerifiedHostKeyFingerprint.Should().Be(FingerprintA);

        fakeClient.CredentialsTransmitted.Should().BeTrue();
        fakeClient.ExecutedCommands.Should().NotBeEmpty();
        fakeClient.ExecutedCommands.Should().Contain(c => c.Contains("uname -m"));
    }

    [Fact]
    public async Task MatchingHostKey_with_private_key_auth_proceeds_and_completes_installation()
    {
        var fakeClient = new FakeSshClient(ServerKeyBytesA, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClient);
        var provisioner = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactory);

        var options = CreateInstallOptions(expectedFingerprint: FingerprintA);
        var creds = CreatePrivateKeyCredentials();

        _registry.AutoConnectOnVm = "vm_123";

        var result = await provisioner.InstallSatelliteAsync(
            "vm_123", creds, options, new Progress<string>(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.VerifiedHostKeyFingerprint.Should().Be(FingerprintA);

        fakeClient.CredentialsTransmitted.Should().BeTrue();
        fakeClient.ExecutedCommands.Should().NotBeEmpty();
        fakeClient.ExecutedCommands.Should().Contain(c => c.Contains("uname -m"));
    }

    // =========================================================================
    // CRITERIO 3 & 4: Claves cambiadas producen un error de seguridad específico
    // =========================================================================

    [Fact]
    public async Task ChangedHostKey_with_password_auth_produces_security_specific_mismatch_error()
    {
        // El servidor presenta Key B, pero esperamos Key A
        var fakeClient = new FakeSshClient(ServerKeyBytesB, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClient);
        var provisioner = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactory);

        var options = CreateInstallOptions(expectedFingerprint: FingerprintA);
        var creds = CreatePasswordCredentials();

        var result = await provisioner.InstallSatelliteAsync(
            "vm_123", creds, options, new Progress<string>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ssh_host_key_mismatch");
        result.ErrorMessage.Should().Contain("ALERTA DE SEGURIDAD");
        result.ErrorMessage.Should().Contain("MITM");
        result.ErrorMessage.Should().Contain(FingerprintA);
        result.ErrorMessage.Should().Contain(FingerprintB);

        // FENCE DE SEGURIDAD: Ningún comando ejecutado y credenciales no usadas
        fakeClient.ExecutedCommands.Should().BeEmpty();
        fakeClient.CredentialsTransmitted.Should().BeFalse();
        fakeClient.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ChangedHostKey_with_private_key_auth_produces_security_specific_mismatch_error()
    {
        // El servidor presenta Key B, pero esperamos Key A
        var fakeClient = new FakeSshClient(ServerKeyBytesB, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClient);
        var provisioner = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactory);

        var options = CreateInstallOptions(expectedFingerprint: FingerprintA);
        var creds = CreatePrivateKeyCredentials();

        var result = await provisioner.InstallSatelliteAsync(
            "vm_123", creds, options, new Progress<string>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ssh_host_key_mismatch");
        result.ErrorMessage.Should().Contain("ALERTA DE SEGURIDAD");
        result.ErrorMessage.Should().Contain("MITM");

        // FENCE DE SEGURIDAD
        fakeClient.ExecutedCommands.Should().BeEmpty();
        fakeClient.CredentialsTransmitted.Should().BeFalse();
        fakeClient.IsConnected.Should().BeFalse();
    }

    // =========================================================================
    // CRITERIO 5: Flujo explícito de rotación de clave de host
    // =========================================================================

    [Fact]
    public async Task HostKeyRotation_workflow_allows_updating_trusted_key()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = Vm.Register(Aethra.Shared.Kernel.Primitives.Slug.Create("test-vm").Value, "Test VM", now).Vm;

        // 1. Inicialmente se fija Fingerprint A
        vm.SetSshHostKeyFingerprint(FingerprintA, now);
        vm.SshHostKeyFingerprint.Should().Be(FingerprintA);

        // 2. Servidor SSH rota a Fingerprint B: la conexión con Fingerprint A falla
        var fakeClientRotated = new FakeSshClient(ServerKeyBytesB, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClientRotated);
        var provisioner = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactory);

        var optionsBefore = CreateInstallOptions(expectedFingerprint: vm.SshHostKeyFingerprint);
        var resultBefore = await provisioner.InstallSatelliteAsync(
            vm.Id.ToString(), CreatePasswordCredentials(), optionsBefore, new Progress<string>(), CancellationToken.None);

        resultBefore.Success.Should().BeFalse();
        resultBefore.ErrorCode.Should().Be("ssh_host_key_mismatch");

        // 3. El operador ejecuta rotación explícita
        vm.RotateSshHostKeyFingerprint(FingerprintB, now.AddMinutes(5));
        vm.SshHostKeyFingerprint.Should().Be(FingerprintB);
        vm.InstallLog.Should().Contain("host_key_rotated");

        // 4. Siguiente intento con la nueva clave rotada tiene éxito
        var optionsAfter = CreateInstallOptions(expectedFingerprint: vm.SshHostKeyFingerprint);
        _registry.AutoConnectOnVm = vm.Id.ToString();

        var fakeClientSuccess = new FakeSshClient(ServerKeyBytesB, "ssh-ed25519");
        var clientFactorySuccess = new FakeSshClientFactory(fakeClientSuccess);
        var provisionerSuccess = new RenciSshProvisioner(_registry, NullLogger<RenciSshProvisioner>.Instance, clientFactorySuccess);

        var resultAfter = await provisionerSuccess.InstallSatelliteAsync(
            vm.Id.ToString(), CreatePasswordCredentials(), optionsAfter, new Progress<string>(), CancellationToken.None);

        resultAfter.Success.Should().BeTrue();
        resultAfter.VerifiedHostKeyFingerprint.Should().Be(FingerprintB);
    }

    // =========================================================================
    // SONDEO PREVIO (Probe): devuelve huella sin enviar credenciales
    // =========================================================================

    [Fact]
    public async Task ProbeAsync_retrieves_host_key_fingerprint_without_credentials()
    {
        var fakeClient = new FakeSshClient(ServerKeyBytesA, "ssh-ed25519");
        var clientFactory = new FakeSshClientFactory(fakeClient);
        var prober = new RenciSshHostKeyProber(clientFactory, NullLogger<RenciSshHostKeyProber>.Instance);

        var result = await prober.ProbeAsync("192.168.1.100", 22, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.HostKeyType.Should().Be("ssh-ed25519");
        result.Fingerprint.Should().Be(FingerprintA);

        // En sondeo de host nunca se envían credenciales de usuario
        fakeClient.CredentialsTransmitted.Should().BeFalse();
        fakeClient.ExecutedCommands.Should().BeEmpty();
    }

    // =========================================================================
    // TEST DOUBLES (Abstracción sin red real)
    // =========================================================================

    private sealed class FakeSshClientFactory(FakeSshClient client) : ISshClientFactory
    {
        public ISshClient CreateClient(ConnectionInfo connectionInfo)
        {
            client.ConnectionInfo = connectionInfo;
            return client;
        }
    }

    private sealed class FakeSshClient(byte[] serverHostKey, string serverHostKeyName) : ISshClient
    {
        public event EventHandler<SshHostKeyReceivedEventArgs>? HostKeyReceived;

        public ConnectionInfo ConnectionInfo { get; set; } = null!;
        public bool IsConnected { get; private set; }
        public bool CredentialsTransmitted { get; private set; }
        public List<string> ExecutedCommands { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            // Simular recepción de clave de host SSH durante KEX
            var args = new SshHostKeyReceivedEventArgs(serverHostKeyName, serverHostKey, 256);
            HostKeyReceived?.Invoke(this, args);

            if (!args.CanTrust)
            {
                // El handshake se aborta inmediatamente si la política rechaza la clave
                throw new SshConnectionException("Key exchange was not finished: Host key not trusted.");
            }

            // Clave aceptada: autenticación permitida
            IsConnected = true;
            CredentialsTransmitted = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        public ISshCommand CreateCommand(string commandText)
        {
            ExecutedCommands.Add(commandText);
            return new FakeSshCommand(commandText);
        }

        public void Dispose() => Disconnect();
    }

    private sealed class FakeSshCommand(string commandText) : ISshCommand
    {
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(1);
        public string Error => string.Empty;
        public int? ExitStatus => 0;

        public string Execute()
        {
            if (commandText.StartsWith("uname -m", StringComparison.Ordinal))
            {
                return "x86_64";
            }
            if (commandText.Contains("command -v docker", StringComparison.Ordinal))
            {
                return "OK";
            }
            return "OK";
        }

        public void CancelAsync() { }
        public void Dispose() { }
    }

    private sealed class FakeSatelliteConnectionRegistry : ISatelliteConnectionRegistry
    {
        public string? AutoConnectOnVm { get; set; }

        public void Register(string vmId, string connectionId) { }
        public void Unregister(string vmId, string connectionId) { }
        public bool IsConnected(string vmId) => AutoConnectOnVm == vmId;
        public string? GetConnectionId(string vmId) => AutoConnectOnVm == vmId ? "conn_123" : null;
        public IReadOnlyCollection<string> ConnectedVmIds => AutoConnectOnVm is not null ? [AutoConnectOnVm] : [];
    }
}
