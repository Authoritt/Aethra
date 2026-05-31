using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using FluentValidation;

namespace Aethra.Modules.Projects.UseCases.Projects.Commands;

/// <summary>
/// Inspecciona un repo Git y devuelve apps candidatas para crear.
///
/// F1 (esta versión): heurística simple sin clonar — asume Dockerfile en raíz, propone una App.
/// F4: clonado shallow real + scan de Dockerfiles en subcarpetas + parse de docker-compose.
/// </summary>
public sealed record DiscoverRepoCommand(string RepoUrl, string? Branch = null) : ICommand<DiscoverRepoResult>;

public sealed record DiscoverRepoResult(
    string RepoUrl,
    string Branch,
    IReadOnlyList<SuggestedApp> SuggestedApps,
    IReadOnlyList<string> Warnings);

public sealed record SuggestedApp(
    string SuggestedSlug,
    string SuggestedName,
    string BaseDirectory,
    string DockerfilePath,
    int? SuggestedPort,
    IReadOnlyList<string> WatchPaths);

public sealed class DiscoverRepoValidator : AbstractValidator<DiscoverRepoCommand>
{
    public DiscoverRepoValidator()
    {
        RuleFor(c => c.RepoUrl).NotEmpty();
    }
}

internal sealed class DiscoverRepoHandler : ICommandHandler<DiscoverRepoCommand, DiscoverRepoResult>
{
    public Task<Result<DiscoverRepoResult>> Handle(DiscoverRepoCommand request, CancellationToken cancellationToken)
    {
        var urlResult = GitRepoUrl.Create(request.RepoUrl);
        if (urlResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<DiscoverRepoResult>(urlResult.Error));
        }

        var url = urlResult.Value;
        var branch = string.IsNullOrWhiteSpace(request.Branch) ? "main" : request.Branch.Trim();
        var repoName = url.SuggestRepoName();
        var slug = Slug.Suggest(repoName);

        // F1: heurística sin clonar — proponemos una sola App con Dockerfile en raíz.
        var suggestion = new SuggestedApp(
            SuggestedSlug: slug.Value,
            SuggestedName: repoName,
            BaseDirectory: "/",
            DockerfilePath: "Dockerfile",
            SuggestedPort: null,
            WatchPaths: []);

        var warnings = new List<string>
        {
            "F1: el discover usa heurística sin clonar el repo. " +
            "En F4 escaneará Dockerfiles y compose-files reales del repo.",
        };

        return Task.FromResult(Result.Success(new DiscoverRepoResult(
            RepoUrl: url.Value,
            Branch: branch,
            SuggestedApps: [suggestion],
            Warnings: warnings)));
    }
}
