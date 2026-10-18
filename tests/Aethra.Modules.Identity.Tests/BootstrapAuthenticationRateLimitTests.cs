using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class BootstrapAuthenticationRateLimitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Null_or_blank_email_is_rejected_before_normalization_and_throttling(string? email)
    {
        var limiter = new RecordingAttemptLimiter();
        var store = Store();

        var accepted = await store.ValidateCredentialsAsync(
            email,
            "irrelevant",
            limiter,
            CancellationToken.None);

        accepted.Should().BeFalse();
        limiter.AcquiredEmails.Should().BeEmpty();
        limiter.ResetEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task Foreign_email_is_rejected_without_creating_a_throttle_partition()
    {
        var limiter = new RecordingAttemptLimiter();
        var store = Store();

        var accepted = await store.ValidateCredentialsAsync(
            "attacker@example.com",
            "irrelevant",
            limiter,
            CancellationToken.None);

        accepted.Should().BeFalse();
        limiter.AcquiredEmails.Should().BeEmpty();
        limiter.ResetEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task Bootstrap_account_acquires_only_its_canonical_partition()
    {
        var limiter = new RecordingAttemptLimiter();
        var store = Store();

        var accepted = await store.ValidateCredentialsAsync(
            "  ADMIN@EXAMPLE.COM ",
            "wrong",
            limiter,
            CancellationToken.None);

        accepted.Should().BeFalse();
        limiter.AcquiredEmails.Should().Equal("admin@example.com");
        limiter.ResetEmails.Should().BeEmpty();
    }

    private static SingleUserStore Store()
        => new(Options.Create(new IdentityOptions
        {
            AdminEmail = "Admin@Example.com",
            AdminPasswordHash = "not-used-by-rejection-tests",
        }));

    private sealed class RecordingAttemptLimiter : IAuthenticationAttemptLimiter
    {
        public List<string> AcquiredEmails { get; } = [];
        public List<string> ResetEmails { get; } = [];

        public ValueTask<AuthenticationAttemptLease> AcquirePasswordAsync(
            string email,
            CancellationToken cancellationToken)
        {
            AcquiredEmails.Add(email);
            return ValueTask.FromResult(new AuthenticationAttemptLease(
                true,
                1,
                DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        public ValueTask ResetPasswordAsync(string email, CancellationToken cancellationToken)
        {
            ResetEmails.Add(email);
            return ValueTask.CompletedTask;
        }
    }
}
