namespace Aethra.Modules.Deployments.Domain.Deployment;

/// <summary>
/// Origen del deployment.
/// <list type="bullet">
///   <item><c>BuildAutomatic</c>: encolado por el suscriptor de <c>BuildCompletedIntegrationEvent</c>
///         para las Instances del Template con <c>AutoDeployOnNewBuild = true</c>.</item>
///   <item><c>Manual</c>: el operador disparó el deploy desde la UI/API para un Build + Instance
///         específicos.</item>
///   <item><c>Promote</c>: el operador "promueve" un Deployment exitoso a otra Instance reutilizando
///         la misma imagen (típico flujo dev → staging → prod).</item>
/// </list>
/// </summary>
public enum DeploymentTrigger
{
    BuildAutomatic = 0,
    Manual = 1,
    Promote = 2,
    Rollback = 3,
}
