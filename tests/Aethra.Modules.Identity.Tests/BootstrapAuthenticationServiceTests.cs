using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class BootstrapAuthenticationServiceTests
{
    private const string AdminEmail = "admin@aethra.local";
    private const string AdminPassword = "test-bootstrap-password-123";

    [Fact]
    public async Task When_persisted_users_exist_outcome_is_always_UsePersistedUsers()
    {
        var (service, logger) = CreateService(allowBootstrap: true, isDevelopment: false);

        var outcome = await service.AuthenticateAsync(
            hasPersistedUsers: true,
            email: AdminEmail,
            password: AdminPassword,
            cancellationToken: CancellationToken.None);

        outcome.Should().Be(BootstrapAuthenticationOutcome.UsePersistedUsers);
        logger.LoggedEvents.Should().BeEmpty("no debe loguear intento de bootstrap si hay usuarios persistidos");
        logger.LoggedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task When_no_persisted_users_and_bootstrap_disabled_outcome_is_Disabled()
    {
        var (service, logger) = CreateService(allowBootstrap: false, isDevelopment: false);

        var outcome = await service.AuthenticateAsync(
            hasPersistedUsers: false,
            email: AdminEmail,
            password: AdminPassword,
            cancellationToken: CancellationToken.None);

        outcome.Should().Be(BootstrapAuthenticationOutcome.Disabled);
        logger.LoggedEvents.Should().Contain(e => e.Id == 2100 && e.Name == "BootstrapLoginAttempted");
        logger.LoggedEvents.Should().Contain(e => e.Id == 2101 && e.Name == "BootstrapLoginRejected");
        logger.LoggedMessages.Should().Contain(m => m.Contains("rechazada porque Identity:AllowBootstrapLogin esta deshabilitado"));
        logger.LoggedMessages.Should().NotContain(m => m.Contains(AdminPassword));
    }

    [Fact]
    public async Task When_no_persisted_users_and_bootstrap_enabled_with_wrong_password_outcome_is_InvalidCredentials()
    {
        var (service, logger) = CreateService(allowBootstrap: true, isDevelopment: false);

        var outcome = await service.AuthenticateAsync(
            hasPersistedUsers: false,
            email: AdminEmail,
            password: "wrong-password",
            cancellationToken: CancellationToken.None);

        outcome.Should().Be(BootstrapAuthenticationOutcome.InvalidCredentials);
        logger.LoggedEvents.Should().Contain(e => e.Id == 2100 && e.Name == "BootstrapLoginAttempted");
        logger.LoggedEvents.Should().Contain(e => e.Id == 2102 && e.Name == "BootstrapLoginInvalidCredentials");
        logger.LoggedMessages.Should().Contain(m => m.Contains("rechazada por credenciales invalidas"));
        logger.LoggedMessages.Should().NotContain(m => m.Contains("wrong-password"));
    }

    [Fact]
    public async Task When_no_persisted_users_and_bootstrap_enabled_with_correct_password_outcome_is_Succeeded()
    {
        var (service, logger) = CreateService(allowBootstrap: true, isDevelopment: false);

        var outcome = await service.AuthenticateAsync(
            hasPersistedUsers: false,
            email: AdminEmail,
            password: AdminPassword,
            cancellationToken: CancellationToken.None);

        outcome.Should().Be(BootstrapAuthenticationOutcome.Succeeded);
        logger.LoggedEvents.Should().Contain(e => e.Id == 2100 && e.Name == "BootstrapLoginAttempted");
        logger.LoggedEvents.Should().Contain(e => e.Id == 2103 && e.Name == "BootstrapLoginSucceeded");
        logger.LoggedMessages.Should().Contain(m => m.Contains("Autenticacion por bootstrap concedida"));
        logger.LoggedMessages.Should().NotContain(m => m.Contains(AdminPassword));
    }

    [Fact]
    public async Task Development_environment_enables_bootstrap_by_default()
    {
        var (service, logger) = CreateService(allowBootstrap: false, isDevelopment: true);

        service.IsBootstrapEnabled.Should().BeTrue();

        var outcome = await service.AuthenticateAsync(
            hasPersistedUsers: false,
            email: AdminEmail,
            password: AdminPassword,
            cancellationToken: CancellationToken.None);

        outcome.Should().Be(BootstrapAuthenticationOutcome.Succeeded);
        logger.LoggedEvents.Should().Contain(e => e.Id == 2100 && e.Name == "BootstrapLoginAttempted");
        logger.LoggedEvents.Should().Contain(e => e.Id == 2103 && e.Name == "BootstrapLoginSucceeded");
    }

    [Fact]
    public async Task Production_environment_disables_bootstrap_by_default()
    {
        var (service, logger) = CreateService(allowBootstrap: false, isDevelopment: false);

        service.IsBootstrapEnabled.Should().BeFalse();

        var outcome = await service.AuthenticateAsync(
            hasPersistedUsers: false,
            email: AdminEmail,
            password: AdminPassword,
            cancellationToken: CancellationToken.None);

        outcome.Should().Be(BootstrapAuthenticationOutcome.Disabled);
        logger.LoggedEvents.Should().Contain(e => e.Id == 2101 && e.Name == "BootstrapLoginRejected");
    }

    [Fact]
    public async Task Password_null_returns_InvalidCredentials_when_bootstrap_is_enabled()
    {
        var (service, logger) = CreateService(allowBootstrap: true, isDevelopment: false);

        var outcome = await service.AuthenticateAsync(
            hasPersistedUsers: false,
            email: AdminEmail,
            password: null,
            cancellationToken: CancellationToken.None);

        outcome.Should().Be(BootstrapAuthenticationOutcome.InvalidCredentials);
        logger.LoggedEvents.Should().Contain(e => e.Id == 2100 && e.Name == "BootstrapLoginAttempted");
        logger.LoggedEvents.Should().Contain(e => e.Id == 2102 && e.Name == "BootstrapLoginInvalidCredentials");
    }

    private static (BootstrapAuthenticationService Service, RecordingLogger<BootstrapAuthenticationService> Logger)
        CreateService(bool allowBootstrap, bool isDevelopment)
    {
        var options = Options.Create(new IdentityOptions
        {
            AdminEmail = AdminEmail,
            AdminPasswordSeed = AdminPassword,
            AllowBootstrapLogin = allowBootstrap,
        });

        var store = new SingleUserStore(options);
        var limiter = new PassThroughAttemptLimiter();
        var logger = new RecordingLogger<BootstrapAuthenticationService>();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? Environments.Development : Environments.Production);

        var service = new BootstrapAuthenticationService(store, limiter, options, logger, env);
        return (service, logger);
    }

    private sealed class PassThroughAttemptLimiter : IAuthenticationAttemptLimiter
    {
        public ValueTask<AuthenticationAttemptLease> AcquirePasswordAsync(string email, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AuthenticationAttemptLease(true, 1, DateTimeOffset.UtcNow.AddMinutes(5)));

        public ValueTask ResetPasswordAsync(string email, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<EventId> LoggedEvents { get; } = [];
        public List<string> LoggedMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LoggedEvents.Add(eventId);
            LoggedMessages.Add(formatter(state, exception));
        }
    }
}
