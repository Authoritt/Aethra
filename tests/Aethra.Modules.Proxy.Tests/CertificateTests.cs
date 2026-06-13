using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Domain.Events;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Proxy.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Certificate"/> (TLS vía ACME): ciclo de vida
/// Pending → Issued ↔ Renewing / Failed, el cálculo de <c>RenewAfter = NotAfter - renewBeforeDays</c>,
/// y el branching que decide si <see cref="Certificate.MarkIssued"/> emite el evento de issued
/// (primera emisión) o de renewed (re-emisión sobre un cert ya emitido / en renovación).
/// </summary>
public sealed class CertificateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Hostname Host => Hostname.Create("app.example.com").Value;
    private static Certificate NewRequested() => Certificate.Request(Host);

    [Fact]
    public void Request_starts_pending_and_raises_requested_event()
    {
        var cert = Certificate.Request(Host);

        cert.Status.Should().Be(CertificateStatus.Pending);
        cert.PfxCipherText.Should().BeNull();
        cert.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CertificateRequestedEvent>();
    }

    [Fact]
    public void MarkIssued_from_pending_sets_issued_and_computes_renew_after()
    {
        var cert = NewRequested();
        cert.ClearDomainEvents();
        var notBefore = Now;
        var notAfter = Now.AddDays(90);

        cert.MarkIssued("pfx-cipher==", notBefore, notAfter, renewBeforeDays: 30, Now);

        cert.Status.Should().Be(CertificateStatus.Issued);
        cert.PfxCipherText.Should().Be("pfx-cipher==");
        cert.NotBefore.Should().Be(notBefore);
        cert.NotAfter.Should().Be(notAfter);
        cert.RenewAfter.Should().Be(notAfter.AddDays(-30));
        cert.IssuedAt.Should().Be(Now);
        cert.LastError.Should().BeNull();
        cert.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CertificateIssuedDomainEvent>();
    }

    [Fact]
    public void MarkIssued_when_already_issued_raises_renewed_event()
    {
        var cert = NewRequested();
        cert.MarkIssued("c1", Now, Now.AddDays(90), 30, Now);
        cert.ClearDomainEvents();

        cert.MarkIssued("c2", Now, Now.AddDays(90), 30, Now);

        cert.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CertificateRenewedDomainEvent>();
    }

    [Fact]
    public void MarkIssued_after_renewing_raises_renewed_event_and_swaps_pfx()
    {
        var cert = NewRequested();
        cert.MarkIssued("c1", Now, Now.AddDays(90), 30, Now);
        cert.MarkRenewing();
        cert.ClearDomainEvents();

        cert.MarkIssued("c2", Now.AddDays(80), Now.AddDays(170), 30, Now.AddDays(80));

        cert.Status.Should().Be(CertificateStatus.Issued);
        cert.PfxCipherText.Should().Be("c2");
        cert.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CertificateRenewedDomainEvent>();
    }

    [Fact]
    public void MarkIssued_throws_on_blank_pfx()
    {
        var cert = NewRequested();

        var act = () => cert.MarkIssued("   ", Now, Now.AddDays(90), 30, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MarkIssued_throws_on_non_positive_renew_before_days(int days)
    {
        var cert = NewRequested();

        var act = () => cert.MarkIssued("cipher", Now, Now.AddDays(90), days, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkRenewing_from_issued_succeeds()
    {
        var cert = NewRequested();
        cert.MarkIssued("c1", Now, Now.AddDays(90), 30, Now);

        cert.MarkRenewing();

        cert.Status.Should().Be(CertificateStatus.Renewing);
    }

    [Fact]
    public void MarkRenewing_from_failed_succeeds()
    {
        var cert = NewRequested();
        cert.MarkFailed("acme error");

        cert.MarkRenewing();

        cert.Status.Should().Be(CertificateStatus.Renewing);
    }

    [Fact]
    public void MarkRenewing_from_pending_throws()
    {
        var cert = NewRequested();

        var act = () => cert.MarkRenewing();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkFailed_sets_failed_preserves_pfx_and_raises_event()
    {
        var cert = NewRequested();
        cert.MarkIssued("c1", Now, Now.AddDays(90), 30, Now);
        cert.ClearDomainEvents();

        cert.MarkFailed("acme rate limited");

        cert.Status.Should().Be(CertificateStatus.Failed);
        cert.LastError.Should().Be("acme rate limited");
        cert.PfxCipherText.Should().Be("c1");
        cert.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CertificateFailedEvent>();
    }
}
