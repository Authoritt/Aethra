using Aethra.Modules.Projects.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

public sealed class ProjectsDbFixture : IAsyncLifetime
{
    private int _databaseId;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<ProjectsDbContext> CreateCleanDbContextAsync()
    {
        var databaseName = $"projects-tests-{Interlocked.Increment(ref _databaseId)}";
        var db = new ProjectsDbContext(new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        return await Task.FromResult(db).ConfigureAwait(false);
    }
}
