namespace Aethra.Modules.Vms.Domain;

/// <summary>
/// Estado de instalación del satélite en una VM. Lo controla el provisioner SSH:
/// <para>
/// <c>NotInstalled</c> es el default tras registrar la VM.<br/>
/// <c>Installing</c> mientras el provisioner SSH está corriendo el script.<br/>
/// <c>Installed</c> cuando el handshake del satélite llega y queda registrado en el registry.<br/>
/// <c>Failed</c> si el provisioner aborta antes de poder verificar el handshake.
/// </para>
/// </summary>
public enum InstallStatus
{
    NotInstalled = 0,
    Installing = 1,
    Installed = 2,
    Failed = 3,
}
