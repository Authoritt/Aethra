using System.Text.Json.Serialization;

namespace Aethra.Modules.Deployments.Webhooks;

/// <summary>
/// F12.3 — subset del payload de un <c>pull_request</c> event de GitHub. Capturamos solo lo
/// necesario para Branch-per-Instance + Preview deployments:
/// - <c>action</c>: <c>opened</c> / <c>reopened</c> / <c>synchronize</c> / <c>closed</c>.
/// - <c>number</c>: número del PR (para componer <c>refs/pull/N/head</c> y slug <c>pr-N</c>).
/// - <c>pull_request.head.sha</c>: HEAD del PR (commit a buildar).
/// - <c>pull_request.user.login</c>: handle GitHub del autor (mapeado a User Aethra).
/// - <c>pull_request.labels</c>: si hay <c>aethra-preview</c>, forzamos preview aunque
///   <c>Template.AutoPreviewPullRequests = false</c>.
/// - <c>repository</c>: para hacer lookup de Templates como en el flujo de push.
/// </summary>
public sealed class GitHubPullRequestPayload
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("pull_request")]
    public GitHubPullRequest? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepository? Repository { get; set; }

    public IEnumerable<string> CandidateRepoUrls()
    {
        if (Repository is null)
        {
            yield break;
        }
        if (!string.IsNullOrWhiteSpace(Repository.CloneUrl))
        {
            yield return Repository.CloneUrl;
        }
        if (!string.IsNullOrWhiteSpace(Repository.SshUrl))
        {
            yield return Repository.SshUrl;
        }
        if (!string.IsNullOrWhiteSpace(Repository.HtmlUrl))
        {
            yield return Repository.HtmlUrl;
        }
    }
}

public sealed class GitHubPullRequest
{
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("comments_url")]
    public string? CommentsUrl { get; set; }

    [JsonPropertyName("user")]
    public GitHubUser? User { get; set; }

    [JsonPropertyName("head")]
    public GitHubPullRequestRef? Head { get; set; }

    [JsonPropertyName("base")]
    public GitHubPullRequestRef? Base { get; set; }

    [JsonPropertyName("labels")]
    public List<GitHubLabel> Labels { get; set; } = [];
}

public sealed class GitHubPullRequestRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("sha")]
    public string? Sha { get; set; }
}

public sealed class GitHubUser
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}

public sealed class GitHubLabel
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
