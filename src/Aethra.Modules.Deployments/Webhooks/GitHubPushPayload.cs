using System.Text.Json.Serialization;

namespace Aethra.Modules.Deployments.Webhooks;

/// <summary>
/// Subset del payload de un <c>push</c> event de GitHub. Solo capturamos los campos
/// que necesitamos para fan-out:
/// - ref → "refs/heads/main" → branch
/// - after → SHA del commit HEAD tras el push
/// - commits[] → cada commit incluye added/modified/removed paths
/// - repository.clone_url / html_url → para hacer lookup de Applications
/// </summary>
public sealed class GitHubPushPayload
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("after")]
    public string? After { get; set; }

    [JsonPropertyName("before")]
    public string? Before { get; set; }

    [JsonPropertyName("commits")]
    public List<GitHubCommit> Commits { get; set; } = [];

    [JsonPropertyName("head_commit")]
    public GitHubCommit? HeadCommit { get; set; }

    [JsonPropertyName("pusher")]
    public GitHubPusher? Pusher { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepository? Repository { get; set; }

    public string? Branch =>
        Ref?.StartsWith("refs/heads/", StringComparison.Ordinal) == true
            ? Ref["refs/heads/".Length..]
            : null;

    /// <summary>
    /// SHA del commit HEAD tras el push, resolviendo en este orden:
    /// <c>after</c> (más fiable, viene incluso en push de tags) → <c>head_commit.id</c>
    /// (fallback) → <c>null</c>. Los consumidores que necesiten un valor obligatorio
    /// deben validar antes de encolar el build.
    /// </summary>
    public string? HeadSha => After ?? HeadCommit?.Id;

    public IReadOnlySet<string> AllAffectedPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in Commits)
        {
            foreach (var p in c.Added)
            {
                paths.Add(p);
            }
            foreach (var p in c.Modified)
            {
                paths.Add(p);
            }
            foreach (var p in c.Removed)
            {
                paths.Add(p);
            }
        }
        return paths;
    }

    /// <summary>URLs candidatas para hacer lookup de Applications (clone_url, ssh_url, html_url).</summary>
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

public sealed class GitHubCommit
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("added")]
    public List<string> Added { get; set; } = [];

    [JsonPropertyName("modified")]
    public List<string> Modified { get; set; } = [];

    [JsonPropertyName("removed")]
    public List<string> Removed { get; set; } = [];
}

public sealed class GitHubPusher
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public sealed class GitHubRepository
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("clone_url")]
    public string? CloneUrl { get; set; }

    [JsonPropertyName("ssh_url")]
    public string? SshUrl { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}
