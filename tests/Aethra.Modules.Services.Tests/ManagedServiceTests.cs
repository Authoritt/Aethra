using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Domain.Events;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Invariantes del agregado <see cref="ManagedService"/>: el factory <c>Create</c> (provisión nueva)
/// vs <c>Adopt</c> (registro de un contenedor existente), las transiciones de estado y la validación
/// de <see cref="BackupPolicy"/>.
/// </summary>
public sealed class ManagedServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Cipher = [1, 2, 3, 4];

    // ---------- Create ----------

    [Fact]
    public void Create_starts_in_provisioning_and_lowercases_slug_and_container_name()
    {
        var svc = ManagedService.Create("Postgres-Main", "Postgres Main", ServiceType.Postgres,
            "16", "vm_ABC", "postgres:16-alpine", Cipher, Now);

        svc.Status.Should().Be(ManagedServiceStatus.Provisioning);
        svc.Slug.Should().Be("postgres-main");
        svc.ContainerName.Should().Be("postgres-main");
        svc.ProvisionedAt.Should().BeNull();
        svc.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_uses_default_internal_port_for_the_type()
    {
        var pg = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);
        var redis = ManagedService.Create("rd", "rd", ServiceType.Redis, "7", "vm", "img", Cipher, Now);

        pg.InternalPort.Should().Be(5432);
        redis.InternalPort.Should().Be(6379);
    }

    [Fact]
    public void Create_honors_explicit_port_override()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now,
            internalPortOverride: 6543);

        svc.InternalPort.Should().Be(6543);
    }

    [Fact]
    public void Create_defaults_network_to_shared_per_vm_lowercased()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "VM_Abc", "img", Cipher, Now);

        svc.NetworkName.Should().Be("aethra_shared_vm_abc");
    }

    [Fact]
    public void Create_honors_explicit_network_name()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now,
            networkName: "aethra-net");

        svc.NetworkName.Should().Be("aethra-net");
    }

    [Fact]
    public void Create_raises_created_event_with_matching_data()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm_X", "img", Cipher, Now);

        var evt = svc.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ManagedServiceCreatedEvent>().Subject;
        evt.ServiceId.Should().Be(svc.Id);
        evt.Type.Should().Be(ServiceType.Postgres);
        evt.Slug.Should().Be("pg");
        evt.TargetVmId.Should().Be("vm_X");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_when_slug_is_blank(string slug)
    {
        var act = () => ManagedService.Create(slug, "name", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_when_target_vm_is_blank()
    {
        var act = () => ManagedService.Create("pg", "name", ServiceType.Postgres, "16", " ", "img", Cipher, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_when_admin_credentials_cipher_is_empty()
    {
        var act = () => ManagedService.Create("pg", "name", ServiceType.Postgres, "16", "vm", "img", [], Now);

        act.Should().Throw<ArgumentException>();
    }

    // ---------- Adopt ----------

    [Fact]
    public void Adopt_starts_ready_with_provisioned_at_and_no_event()
    {
        var svc = ManagedService.Adopt("aethra-postgres", "Aethra Postgres", ServiceType.Postgres, "16",
            "vm_ET3", "aethra-postgres", "postgres:16-alpine", 5432, "aethra-net", Cipher, Now);

        svc.Status.Should().Be(ManagedServiceStatus.Ready);
        svc.ProvisionedAt.Should().Be(Now);
        svc.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Adopt_lowercases_explicit_container_name()
    {
        var svc = ManagedService.Adopt("svc", "svc", ServiceType.Redis, "7", "vm", "MyContainer",
            "redis:7", 6379, "aethra-net", Cipher, Now);

        svc.ContainerName.Should().Be("mycontainer");
    }

    [Fact]
    public void Adopt_defaults_blank_image_to_external_marker()
    {
        var svc = ManagedService.Adopt("svc", "svc", ServiceType.Redis, "7", "vm", "c",
            "  ", 6379, "aethra-net", Cipher, Now);

        svc.Image.Should().Be("(external)");
    }

    [Fact]
    public void Adopt_defaults_blank_network_to_aethra_net()
    {
        var svc = ManagedService.Adopt("svc", "svc", ServiceType.Redis, "7", "vm", "c",
            "redis:7", 6379, "", Cipher, Now);

        svc.NetworkName.Should().Be("aethra-net");
    }

    [Theory]
    [InlineData("", "vm", "container")]
    [InlineData("slug", "", "container")]
    [InlineData("slug", "vm", "")]
    public void Adopt_throws_when_required_field_is_blank(string slug, string vm, string container)
    {
        var act = () => ManagedService.Adopt(slug, "name", ServiceType.Postgres, "16", vm, container,
            "img", 5432, "aethra-net", Cipher, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Adopt_throws_when_admin_credentials_cipher_is_empty()
    {
        var act = () => ManagedService.Adopt("slug", "name", ServiceType.Postgres, "16", "vm", "c",
            "img", 5432, "aethra-net", [], Now);

        act.Should().Throw<ArgumentException>();
    }

    // ---------- Transitions ----------

    [Fact]
    public void MarkProvisioned_moves_to_ready_clears_errors_and_raises_event()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);
        svc.MarkFailed("boom", "earlier failure", Now);
        svc.ClearDomainEvents();

        var later = Now.AddMinutes(5);
        svc.MarkProvisioned(later);

        svc.Status.Should().Be(ManagedServiceStatus.Ready);
        svc.ProvisionedAt.Should().Be(later);
        svc.ErrorCode.Should().BeNull();
        svc.ErrorMessage.Should().BeNull();
        svc.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ManagedServiceProvisionedEvent>();
    }

    [Fact]
    public void MarkProvisioned_is_a_noop_when_already_ready()
    {
        // Adopt deja el servicio Ready sin eventos: re-provisionar no debe duplicar nada.
        var svc = ManagedService.Adopt("svc", "svc", ServiceType.Redis, "7", "vm", "c",
            "redis:7", 6379, "aethra-net", Cipher, Now);

        svc.MarkProvisioned(Now.AddMinutes(1));

        svc.Status.Should().Be(ManagedServiceStatus.Ready);
        svc.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkFailed_sets_failed_with_error_and_raises_event()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);
        svc.ClearDomainEvents();

        svc.MarkFailed("provision.error", "no disk", Now);

        svc.Status.Should().Be(ManagedServiceStatus.Failed);
        svc.ErrorCode.Should().Be("provision.error");
        svc.ErrorMessage.Should().Be("no disk");
        var evt = svc.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ManagedServiceFailedEvent>().Subject;
        evt.ErrorCode.Should().Be("provision.error");
    }

    [Fact]
    public void MarkStopped_sets_stopped_status()
    {
        var svc = ManagedService.Adopt("svc", "svc", ServiceType.Redis, "7", "vm", "c",
            "redis:7", 6379, "aethra-net", Cipher, Now);

        svc.MarkStopped(Now.AddMinutes(1));

        svc.Status.Should().Be(ManagedServiceStatus.Stopped);
    }

    // ---------- UpdateInfo / BackupPolicy ----------

    [Fact]
    public void UpdateInfo_changes_name_and_exposure()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);

        svc.UpdateInfo("  Renamed  ", exposedExternally: true, Now.AddMinutes(1));

        svc.Name.Should().Be("Renamed");
        svc.ExposedExternally.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateInfo_throws_when_name_is_blank(string name)
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);

        var act = () => svc.UpdateInfo(name, exposedExternally: false, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetBackupPolicy_accepts_a_valid_policy()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);
        var policy = new BackupPolicy("0 2 * * *", RetentionCount: 7, "volume://backups/pg");

        svc.SetBackupPolicy(policy, Now);

        svc.BackupPolicy.Should().Be(policy);
    }

    [Fact]
    public void SetBackupPolicy_throws_on_invalid_destination_scheme()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);
        var invalid = new BackupPolicy("0 2 * * *", RetentionCount: 7, "ftp://nope");

        var act = () => svc.SetBackupPolicy(invalid, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetBackupPolicy_allows_clearing_with_null()
    {
        var svc = ManagedService.Create("pg", "pg", ServiceType.Postgres, "16", "vm", "img", Cipher, Now);
        svc.SetBackupPolicy(new BackupPolicy("0 2 * * *", 7, "s3://bucket/pg"), Now);

        svc.SetBackupPolicy(null, Now.AddMinutes(1));

        svc.BackupPolicy.Should().BeNull();
    }
}
