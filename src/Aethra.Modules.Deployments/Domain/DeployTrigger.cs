namespace Aethra.Modules.Deployments.Domain;

public enum DeployTrigger
{
    Webhook = 0,
    Manual = 1,
    Scheduled = 2,
}
