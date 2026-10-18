using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class AuthenticationRateLimitTests
{
    [Fact]
    public async Task Repeated_account_failures_stop_before_a_sixth_password_hash()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var limiter = new FakeAttemptLimiter(clock, passwordLimit: 5, TimeSpan.FromMinutes(5));
        var password = new CountingPasswordCodec(_ => false);
        var store = Store(password, limiter, clock, "admin@example.com");

        for (var attempt = 0; attempt < 6; attempt++)
        {
            (await store.ValidateCredentialsAsync("admin@example.com", "wrong", CancellationToken.None))
                .Should().BeNull();
        }

        password.VerifyCalls.Should().Be(5, "el intento bloqueado no debe ejecutar Argon2");
    }

    [Fact]
    public async Task Password_spraying_uses_an_independent_budget_for_each_account()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var limiter = new FakeAttemptLimiter(clock, passwordLimit: 2, TimeSpan.FromMinutes(5));
        var password = new CountingPasswordCodec(_ => false);
        var store = Store(password, limiter, clock, "one@example.com", "two@example.com");

        for (var round = 0; round < 3; round++)
        {
            await store.ValidateCredentialsAsync("one@example.com", "wrong", CancellationToken.None);
            await store.ValidateCredentialsAsync("two@example.com", "wrong", CancellationToken.None);
        }

        password.VerifyCalls.Should().Be(4, "cada cuenta permite dos verificaciones costosas");
        limiter.PartitionCount.Should().Be(2);
    }

    [Fact]
    public async Task Account_budget_recovers_after_five_minute_window_without_extending_it()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var limiter = new FakeAttemptLimiter(clock, passwordLimit: 2, TimeSpan.FromMinutes(5));
        var password = new CountingPasswordCodec(_ => false);
        var store = Store(password, limiter, clock, "admin@example.com");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await store.ValidateCredentialsAsync("admin@example.com", "wrong", CancellationToken.None);
        }
        password.VerifyCalls.Should().Be(2);

        clock.UtcNow = clock.UtcNow.AddMinutes(5).AddSeconds(1);
        await store.ValidateCredentialsAsync("admin@example.com", "wrong", CancellationToken.None);

        password.VerifyCalls.Should().Be(3);
    }

    [Fact]
    public async Task Successful_login_resets_the_account_budget()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var limiter = new FakeAttemptLimiter(clock, passwordLimit: 2, TimeSpan.FromMinutes(5));
        var password = new CountingPasswordCodec(candidate => candidate == "correct");
        var store = Store(password, limiter, clock, "admin@example.com");

        await store.ValidateCredentialsAsync("admin@example.com", "wrong", CancellationToken.None);
        (await store.ValidateCredentialsAsync("admin@example.com", "correct", CancellationToken.None))
            .Should().NotBeNull();
        await store.ValidateCredentialsAsync("admin@example.com", "wrong", CancellationToken.None);
        await store.ValidateCredentialsAsync("admin@example.com", "wrong", CancellationToken.None);

        password.VerifyCalls.Should().Be(4);
    }

    [Fact]
    public void Account_partition_is_normalized_and_non_reversible()
    {
        var normalized = AuthenticationAttemptLimiter.PartitionKey("password", "admin@example.com");

        normalized.Should().Be(AuthenticationAttemptLimiter.PartitionKey("password", "admin@example.com"));
        normalized.Should().HaveLength(64);
        normalized.Should().NotContain("admin@example.com");
    }

    private static EfUserStore Store(
        CountingPasswordCodec password,
        IAuthenticationAttemptLimiter limiter,
        IClock clock,
        params string[] emails)
        => new(
            new FixedUserRepository(emails.Select(email => User.Create(email, [1], null, clock.UtcNow))),
            password,
            clock,
            limiter,
            NullLogger<EfUserStore>.Instance);

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class CountingPasswordCodec(Func<string, bool> verify) : IUserPasswordCodec
    {
        public int VerifyCalls { get; private set; }

        public byte[] HashAndProtect(string plainPassword) => [1];

        public bool Verify(string plainPassword, byte[] storedCipher)
        {
            VerifyCalls++;
            return verify(plainPassword);
        }
    }

    private sealed class FixedUserRepository(IEnumerable<User> users) : IUserRepository
    {
        private readonly IReadOnlyList<User> _users = [.. users];

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
            => Task.FromResult(_users.SingleOrDefault(u => u.Email == User.NormalizeEmail(email)));

        public Task<User?> GetByIdAsync(UserId id, CancellationToken ct)
            => Task.FromResult(_users.SingleOrDefault(u => u.Id == id));

        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct) => Task.FromResult(_users);

        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(_users.Count);
    }

    private sealed class FakeAttemptLimiter(
        IClock clock,
        int passwordLimit,
        TimeSpan passwordWindow) : IAuthenticationAttemptLimiter
    {
        private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);

        public int PartitionCount => _states.Count;

        public ValueTask<AuthenticationAttemptLease> AcquirePasswordAsync(
            string email,
            CancellationToken cancellationToken)
        {
            var key = AuthenticationAttemptLimiter.PartitionKey(
                "password",
                User.NormalizeEmail(email));
            if (!_states.TryGetValue(key, out var state) || state.ExpiresAt <= clock.UtcNow)
            {
                state = new State(0, clock.UtcNow.Add(passwordWindow));
            }

            state = state with { Attempts = state.Attempts + 1 };
            _states[key] = state;
            return ValueTask.FromResult(new AuthenticationAttemptLease(
                state.Attempts <= passwordLimit,
                state.Attempts,
                state.ExpiresAt));
        }

        public ValueTask ResetPasswordAsync(string email, CancellationToken cancellationToken)
        {
            _states.Remove(AuthenticationAttemptLimiter.PartitionKey(
                "password",
                User.NormalizeEmail(email)));
            return ValueTask.CompletedTask;
        }

        private sealed record State(int Attempts, DateTimeOffset ExpiresAt);
    }
}
