using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Shared.Kernel.Time;
using Testcontainers.PostgreSql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class TotpRecoveryExecutionStrategyTests(PostgreSql18Fixture postgres)
    : IClassFixture<PostgreSql18Fixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private const string RecoveryCode = "ABCD2345";

    [Fact]
    public async Task Recovery_login_works_with_the_registered_retrying_execution_strategy()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(postgres.ConnectionString, npgsql =>
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), errorCodesToAdd: null))
            .Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var user = User.Create($"{Guid.NewGuid():N}@example.com", [1], null, Now);
        user.BeginTotpEnrollment("12345678901234567890"u8.ToArray(), Now);
        user.CompleteTotpEnrollment(RecoveryCodes.Pack([RecoveryCode]), Now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var clock = new FixedClock(Now);
        var store = new TotpChallengeStore(new EfTotpChallengePersistence(db), clock);
        const string challengeId = "retrying-recovery-login";
        store.Create(challengeId, user.Id.ToString());
        store.TrySelectActive(challengeId, user.Id.ToString()).Should().BeTrue();

        var verifier = new TotpLoginVerifier(db, new PassthroughCodec(), clock, store);

        (await verifier.VerifyAsync(user, RecoveryCode, CancellationToken.None)).Should().BeTrue();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class PassthroughCodec : ITotpSecretCodec
    {
        public byte[] Protect(byte[] secret) => secret;
        public byte[] Unprotect(byte[] cipher) => cipher;
    }
}

public sealed class PostgreSql18Fixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PostgreSQL 18 test container could not start. " +
                "A running Docker-compatible container runtime is required for this test.",
                ex);
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
