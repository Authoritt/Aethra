using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Deployments.Webhooks;

internal static class WebhookTemplateAuthenticator
{
    public static IReadOnlyList<TemplateForBuildView> FilterAuthenticatedTemplates(
        IReadOnlyList<TemplateForBuildView> matchingTemplates,
        string? signatureHeader,
        byte[] body)
    {
        var authenticated = new List<TemplateForBuildView>(matchingTemplates.Count);

        foreach (var template in matchingTemplates)
        {
            if (GitHubSignatureValidator.Validate(signatureHeader, body, template.WebhookSecret))
            {
                authenticated.Add(template);
            }
        }

        return authenticated;
    }
}
