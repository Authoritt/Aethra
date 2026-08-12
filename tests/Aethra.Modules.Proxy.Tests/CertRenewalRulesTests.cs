using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure.Tls;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Proxy.Tests;

public sealed class CertRenewalRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Decide_renews_issued_certificate_due_before_expiration()
        => CertRenewalRules.Decide(
                CertificateStatus.Issued,
                renewAfter: Now.AddMinutes(-1),
                notAfter: Now.AddDays(10),
                Now)
            .Should().Be(CertRenewalDecision.Renew);

    [Fact]
    public void Decide_expires_due_issued_certificate_when_not_after_passed()
        => CertRenewalRules.Decide(
                CertificateStatus.Issued,
                renewAfter: Now.AddDays(-1),
                notAfter: Now,
                Now)
            .Should().Be(CertRenewalDecision.Expire);

    [Fact]
    public void Decide_skips_failed_certificate_until_its_own_retry_time()
        => CertRenewalRules.Decide(
                CertificateStatus.Failed,
                renewAfter: Now.AddHours(5),
                notAfter: Now.AddDays(10),
                Now)
            .Should().Be(CertRenewalDecision.Skip);

    [Fact]
    public void Decide_renews_failed_certificate_when_its_own_retry_time_arrives()
        => CertRenewalRules.Decide(
                CertificateStatus.Failed,
                renewAfter: Now,
                notAfter: Now.AddDays(10),
                Now)
            .Should().Be(CertRenewalDecision.Renew);

    [Fact]
    public void Decide_keeps_unrelated_due_certificate_renewable_when_another_is_in_backoff()
    {
        var failedInBackoff = CertRenewalRules.Decide(
            CertificateStatus.Failed,
            renewAfter: Now.AddHours(5),
            notAfter: Now.AddDays(10),
            Now);
        var healthyDue = CertRenewalRules.Decide(
            CertificateStatus.Issued,
            renewAfter: Now,
            notAfter: Now.AddDays(10),
            Now);

        failedInBackoff.Should().Be(CertRenewalDecision.Skip);
        healthyDue.Should().Be(CertRenewalDecision.Renew);
    }

    /// <summary>
    /// Estados que NO participan del ciclo de renovacion. Expired NO esta aqui a proposito: un
    /// certificado caducado sigue siendo elegible, porque es justo cuando mas urge renovarlo.
    /// Excluirlo lo dejaria muerto para siempre y el host sin TLS.
    /// </summary>
    [Theory]
    [InlineData(CertificateStatus.Pending)]
    [InlineData(CertificateStatus.Renewing)]
    public void Decide_skips_non_eligible_statuses(CertificateStatus status)
        => CertRenewalRules.Decide(
                status,
                renewAfter: Now,
                notAfter: Now.AddDays(10),
                Now)
            .Should().Be(CertRenewalDecision.Skip);

    [Fact]
    public void NextRetryAfter_is_per_certificate_backoff()
        => CertRenewalRules.NextRetryAfter(Now, TimeSpan.FromHours(6))
            .Should().Be(Now.AddHours(6));

    [Fact]
    public void NextRetryAfter_rejects_non_positive_backoff()
    {
        var act = () => CertRenewalRules.NextRetryAfter(Now, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Un certificado ya CADUCADO sigue siendo elegible para renovarse. Es el caso que mas importa:
    /// si se excluyera, el host se quedaria sin TLS de forma permanente y nadie lo reintentaria.
    /// Lo que el issue pedia era dejar de emitir el evento en bucle, no dejar de renovar.
    /// </summary>
    [Fact]
    public void An_already_expired_certificate_keeps_trying_to_renew()
    {
        var now = DateTimeOffset.UtcNow;

        CertRenewalRules.Decide(
            CertificateStatus.Expired,
            renewAfter: now.AddMinutes(-1),
            notAfter: now.AddDays(-3),
            now).Should().Be(CertRenewalDecision.Renew);
    }

    /// <summary>
    /// El evento de expiracion se emite UNA sola vez: en la transicion. Ya en Expired, la caducidad
    /// no vuelve a ser noticia.
    /// </summary>
    [Fact]
    public void The_expiry_decision_only_happens_on_the_transition()
    {
        var now = DateTimeOffset.UtcNow;

        CertRenewalRules.Decide(
            CertificateStatus.Issued, now.AddMinutes(-1), now.AddDays(-1), now)
            .Should().Be(CertRenewalDecision.Expire);

        CertRenewalRules.Decide(
            CertificateStatus.Expired, now.AddMinutes(-1), now.AddDays(-1), now)
            .Should().NotBe(CertRenewalDecision.Expire);
    }

    /// <summary>Un certificado caducado que aun no toca reintentar respeta su backoff.</summary>
    [Fact]
    public void An_expired_certificate_still_honours_its_backoff()
    {
        var now = DateTimeOffset.UtcNow;

        CertRenewalRules.Decide(
            CertificateStatus.Expired,
            renewAfter: now.AddHours(1),
            notAfter: now.AddDays(-1),
            now).Should().Be(CertRenewalDecision.Skip);
    }
}
