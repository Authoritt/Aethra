using System.Collections.Concurrent;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class TotpChallengeSingleUseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly byte[] Secret = "12345678901234567890"u8.ToArray();

    [Fact]
    public async Task Successful_totp_login_consumes_challenge_and_sequential_replay_is_rejected()
    {
        var persistence = new ConcurrentChallengePersistence();
        var user = NewTotpUser();
        const string challengeId = "sequential";
        NewStore(persistence).Create(challengeId, user.Id.ToString());
        var code = TotpGenerator.Generate(Secret);

        (await CompleteLoginAsync(persistence, user, challengeId, code)).Should().BeTrue();
        (await CompleteLoginAsync(persistence, user, challengeId, code)).Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_successful_totp_logins_across_replicas_allow_exactly_one_consumer()
    {
        var persistence = new ConcurrentChallengePersistence();
        var user = NewTotpUser();
        const string challengeId = "concurrent";
        NewStore(persistence).Create(challengeId, user.Id.ToString());
        var code = TotpGenerator.Generate(Secret);

        using var ready = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var first = CompleteLoginAsync(persistence, user, challengeId, code, ready, release);
        var second = CompleteLoginAsync(persistence, user, challengeId, code, ready, release);
        ready.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        release.Set();

        var results = await Task.WhenAll(first, second);
        results.Count(result => result).Should().Be(1);
    }

    [Fact]
    public async Task Five_failed_attempts_exhaust_challenge_but_earlier_failures_can_retry()
    {
        var persistence = new ConcurrentChallengePersistence();
        const string challengeId = "attempt-limit";
        NewStore(persistence).Create(challengeId, "usr-attempts");

        for (var attempt = 1; attempt <= TotpChallenge.MaxFailedAttempts; attempt++)
        {
            var store = NewStore(persistence);
            store.TrySelectActive(challengeId, "usr-attempts").Should().BeTrue();
            await store.RegisterFailedAttemptAsync(CancellationToken.None);
        }

        NewStore(persistence).TrySelectActive(challengeId, "usr-attempts").Should().BeFalse();
    }

    [Fact]
    public void Challenge_expires_after_documented_fifteen_minute_ttl()
    {
        var persistence = new ConcurrentChallengePersistence();
        const string challengeId = "expired";
        NewStore(persistence).Create(challengeId, "usr-expired");

        var later = new TotpChallengeStore(persistence, new FixedClock(Now.Add(TotpChallenge.Lifetime)));
        later.TrySelectActive(challengeId, "usr-expired").Should().BeFalse();
    }

    [Fact]
    public async Task Jwt_nonce_is_persisted_and_consumed_state_is_seen_by_another_replica()
    {
        var persistence = new ConcurrentChallengePersistence();
        var key = new TotpChallengeSigningKey(Options.Create(new IdentityOptions
        {
            TotpChallengeSigningKey = "shared-test-key-with-at-least-32-bytes",
        }));
        var issuingStore = NewStore(persistence);
        var token = new TotpChallengeTokens(key, issuingStore, new FixedClock(Now)).Issue("usr-token");

        var replicaStore = NewStore(persistence);
        var replicaTokens = new TotpChallengeTokens(key, replicaStore, new FixedClock(Now));
        replicaTokens.ValidateAndGetUserId(token).Should().Be("usr-token");
        (await replicaStore.TryConsumeCurrentAsync(CancellationToken.None)).Should().BeTrue();

        var replayStore = NewStore(persistence);
        var replayTokens = new TotpChallengeTokens(key, replayStore, new FixedClock(Now));
        replayTokens.ValidateAndGetUserId(token).Should().BeNull();
    }

    [Fact]
    public void Oversized_jwt_is_an_invalid_token_not_a_server_error()
    {
        var tokens = NewTokens(new ConcurrentChallengePersistence());
        var oversized = new string('a', 300 * 1024);

        tokens.ValidateAndGetUserId(oversized).Should().BeNull();
        tokens.GetLastValidationError().Should().Be("invalid_token");
    }

    [Fact]
    public void Invalid_jwt_reports_only_a_stable_error_code()
    {
        var tokens = NewTokens(new ConcurrentChallengePersistence());

        tokens.ValidateAndGetUserId("not-a-jwt").Should().BeNull();

        tokens.GetLastValidationError().Should().Be("invalid_token")
            .And.NotContain("Exception")
            .And.NotContain("IDX");
    }

    [Fact]
    public void Challenge_store_failure_propagates_without_exposing_it_as_a_validation_error()
    {
        var token = NewTokens(new ConcurrentChallengePersistence()).Issue("usr-store-failure");
        var tokens = NewTokens(new ThrowingChallengePersistence());

        var validate = () => tokens.ValidateAndGetUserId(token);

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage(ThrowingChallengePersistence.Diagnostic);
        tokens.GetLastValidationError().Should().BeNull();
    }

    [Fact]
    public async Task Password_step_marks_login_without_2fa_but_not_while_second_factor_is_pending()
    {
        var withoutTotp = User.Create("without-totp@example.com", [1], null, Now);
        var withTotp = NewTotpUser();
        var clock = new FixedClock(Now);

        var plainLogin = new EfUserStore(
            new StubUserRepository(withoutTotp), new AcceptingPasswordCodec(), clock);
        var pendingTotpLogin = new EfUserStore(
            new StubUserRepository(withTotp), new AcceptingPasswordCodec(), clock);

        (await plainLogin.ValidateCredentialsAsync(
            withoutTotp.Email, "valid", CancellationToken.None))!.LastLoginAt.Should().Be(Now);
        (await pendingTotpLogin.ValidateCredentialsAsync(
            withTotp.Email, "valid", CancellationToken.None))!.LastLoginAt.Should().BeNull();
    }

    [Fact]
    public void Retention_removes_only_challenges_expired_for_more_than_one_day()
    {
        var pastRetention = new TotpChallenge(
            "past-retention", "usr-old", Now - TotpChallenge.Lifetime
                - TotpChallengeRetention.RetentionAfterExpiry - TimeSpan.FromSeconds(1));
        var recentlyExpired = new TotpChallenge(
            "recently-expired", "usr-recent", Now - TotpChallenge.Lifetime - TimeSpan.FromMinutes(1));
        var active = new TotpChallenge("active", "usr-active", Now);

        var purgeable = new[] { pastRetention, recentlyExpired, active }
            .AsQueryable()
            .WherePastRetention(Now.UtcDateTime)
            .Select(challenge => challenge.Id);

        purgeable.Should().Equal("past-retention");
    }

    private static User NewTotpUser()
    {
        var user = User.Create($"{Guid.NewGuid():N}@example.com", [1], null, Now);
        user.BeginTotpEnrollment(Secret, Now);
        user.CompleteTotpEnrollment([1], Now);
        return user;
    }

    private static async Task<bool> CompleteLoginAsync(
        ConcurrentChallengePersistence persistence,
        User user,
        string challengeId,
        string code,
        CountdownEvent? ready = null,
        ManualResetEventSlim? release = null)
    {
        var store = NewStore(persistence);
        if (!store.TrySelectActive(challengeId, user.Id.ToString()))
        {
            return false;
        }

        ready?.Signal();
        await Task.Yield();
        release?.Wait(TimeSpan.FromSeconds(10));
        await using var db = NewDisconnectedDb();
        var verifier = new TotpLoginVerifier(db, new PassthroughCodec(), new FixedClock(Now), store);
        return await verifier.VerifyAsync(user, code, CancellationToken.None);
    }

    private static TotpChallengeStore NewStore(ITotpChallengePersistence persistence)
        => new(persistence, new FixedClock(Now));

    private static TotpChallengeTokens NewTokens(ITotpChallengePersistence persistence)
    {
        var key = new TotpChallengeSigningKey(Options.Create(new IdentityOptions
        {
            TotpChallengeSigningKey = "shared-test-key-with-at-least-32-bytes",
        }));
        return new TotpChallengeTokens(key, NewStore(persistence), new FixedClock(Now));
    }

    private static IdentityDbContext NewDisconnectedDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        return new IdentityDbContext(options);
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

    private sealed class AcceptingPasswordCodec : IUserPasswordCodec
    {
        public byte[] HashAndProtect(string plainPassword) => [1];
        public bool Verify(string plainPassword, byte[] storedCipher) => true;
    }

    private sealed class StubUserRepository(User user) : IUserRepository
    {
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
            => Task.FromResult<User?>(user);

        public Task<User?> GetByIdAsync(UserId id, CancellationToken ct)
            => Task.FromResult<User?>(user.Id == id ? user : null);

        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<User>>([user]);

        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(1);
    }

    private sealed class ConcurrentChallengePersistence : ITotpChallengePersistence
    {
        private readonly ConcurrentDictionary<string, ChallengeState> _states = new(StringComparer.Ordinal);

        public void Create(TotpChallenge challenge)
        {
            if (!_states.TryAdd(challenge.Id,
                    new ChallengeState(challenge.UserId, challenge.ExpiresAt)))
            {
                throw new InvalidOperationException("Duplicate challenge id.");
            }
        }

        public bool IsActive(string challengeId, string userId, DateTime utcNow)
        {
            if (!_states.TryGetValue(challengeId, out var state))
            {
                return false;
            }
            lock (state)
            {
                return state.UserId == userId && state.IsActive(utcNow);
            }
        }

        public Task<bool> TryConsumeAsync(string challengeId, DateTime utcNow, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_states.TryGetValue(challengeId, out var state))
            {
                return Task.FromResult(false);
            }
            lock (state)
            {
                if (!state.IsActive(utcNow))
                {
                    return Task.FromResult(false);
                }
                state.Consumed = true;
                return Task.FromResult(true);
            }
        }

        public Task RegisterFailedAttemptAsync(string challengeId, DateTime utcNow, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_states.TryGetValue(challengeId, out var state))
            {
                lock (state)
                {
                    if (state.IsActive(utcNow))
                    {
                        state.FailedAttempts++;
                    }
                }
            }
            return Task.CompletedTask;
        }

        private sealed class ChallengeState(string userId, DateTime expiresAt)
        {
            public string UserId { get; } = userId;
            public DateTime ExpiresAt { get; } = expiresAt;
            public int FailedAttempts { get; set; }
            public bool Consumed { get; set; }

            public bool IsActive(DateTime utcNow)
                => !Consumed && ExpiresAt > utcNow && FailedAttempts < TotpChallenge.MaxFailedAttempts;
        }
    }

    private sealed class ThrowingChallengePersistence : ITotpChallengePersistence
    {
        public const string Diagnostic = "postgres-secret-diagnostic";

        public void Create(TotpChallenge challenge) => throw new NotSupportedException();

        public bool IsActive(string challengeId, string userId, DateTime utcNow)
            => throw new InvalidOperationException(Diagnostic);

        public Task<bool> TryConsumeAsync(string challengeId, DateTime utcNow, CancellationToken ct)
            => throw new NotSupportedException();

        public Task RegisterFailedAttemptAsync(
            string challengeId, DateTime utcNow, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
