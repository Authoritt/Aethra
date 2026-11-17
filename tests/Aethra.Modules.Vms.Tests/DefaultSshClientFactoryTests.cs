using Aethra.Modules.Vms.Infrastructure.Provisioning;
using FluentAssertions;
using Renci.SshNet;
using Renci.SshNet.Common;
using Xunit;

namespace Aethra.Modules.Vms.Tests;

public sealed class DefaultSshClientFactoryTests
{
    [Fact]
    public void RenciSshClientAdapter_HostKeyReceived_propagates_CanTrust_false_to_underlying_event()
    {
        var sshClient = new SshClient("127.0.0.1", 22, "testuser", "testpassword");
        var adapter = new RenciSshClientAdapter(sshClient);

        // Obtenemos el handler privado suscrito al evento HostKeyReceived de SshClient
        var type = typeof(SshClient);
        System.Reflection.FieldInfo? field = null;
        while (type != null && field == null)
        {
            field = type.GetField("HostKeyReceived", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field.Should().NotBeNull();
        var handler = (EventHandler<HostKeyEventArgs>?)field!.GetValue(sshClient);
        handler.Should().NotBeNull();

        var e = (HostKeyEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(HostKeyEventArgs));
        typeof(HostKeyEventArgs)
            .GetField("_lazyFingerPrintSHA256", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(e, new Lazy<string>(() => "SHA256:test"));
        typeof(HostKeyEventArgs)
            .GetField("<HostKey>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(e, new byte[] { 1, 2, 3 });
        typeof(HostKeyEventArgs)
            .GetField("<HostKeyName>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(e, "ssh-ed25519");
        e.CanTrust = true; // Valor por defecto de SSH.NET

        bool callbackInvoked = false;
        adapter.HostKeyReceived += (_, args) =>
        {
            callbackInvoked = true;
            args.CanTrust = false; // Decisión explícita del handler de no confiar
        };

        // Disparamos el evento interno de SshClient
        handler!(sshClient, e);

        callbackInvoked.Should().BeTrue();
        // Falla si se borra "e.CanTrust = args.CanTrust" en RenciSshClientAdapter
        e.CanTrust.Should().BeFalse();
    }
}
