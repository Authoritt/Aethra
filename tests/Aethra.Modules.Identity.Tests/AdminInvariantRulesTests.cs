using Aethra.Modules.Identity.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// El invariante "la instalación siempre conserva al menos un administrador activo" se aplicaba en
/// una sola de las rutas que pueden violarlo (desactivar un usuario) y no en la otra (reemplazar sus
/// roles), de modo que una edición de roles corriente podía dejar el sistema sin administradores y
/// obligar a recuperarlo tocando la base de datos a mano.
///
/// <para>Estos tests fijan la decisión compartida por ambas rutas. Son puros a propósito: la
/// alternativa —probarlo a través de los handlers— exigiría una base relacional, que hoy está
/// bloqueada en este repo (ver issue #106).</para>
/// </summary>
public sealed class AdminInvariantRulesTests
{
    // ---- Desactivación ----

    [Fact]
    public void Deactivating_a_non_admin_is_always_allowed()
        => AdminInvariantRules.CanDeactivate(targetIsAdmin: false, otherActiveAdmins: 0)
            .Should().BeTrue("quien no es admin no sostiene el invariante");

    [Fact]
    public void Deactivating_an_admin_is_allowed_while_another_one_survives()
        => AdminInvariantRules.CanDeactivate(targetIsAdmin: true, otherActiveAdmins: 1)
            .Should().BeTrue();

    [Fact]
    public void Deactivating_the_last_active_admin_is_rejected()
        => AdminInvariantRules.CanDeactivate(targetIsAdmin: true, otherActiveAdmins: 0)
            .Should().BeFalse("dejaría la instalación sin nadie que pueda crear usuarios");

    // ---- Reemplazo de roles (la ruta que no estaba cubierta) ----

    [Fact]
    public void Keeping_the_admin_role_is_always_allowed()
        => AdminInvariantRules.CanReplaceRoles(
                targetIsActive: true, targetIsAdminNow: true, targetKeepsAdmin: true, otherActiveAdmins: 0)
            .Should().BeTrue("conservar el rol no reduce el número de administradores");

    [Fact]
    public void Granting_the_admin_role_is_always_allowed()
        => AdminInvariantRules.CanReplaceRoles(
                targetIsActive: true, targetIsAdminNow: false, targetKeepsAdmin: true, otherActiveAdmins: 0)
            .Should().BeTrue("darlo solo puede aumentar el número de administradores");

    [Fact]
    public void A_non_admin_changing_roles_is_always_allowed()
        => AdminInvariantRules.CanReplaceRoles(
                targetIsActive: true, targetIsAdminNow: false, targetKeepsAdmin: false, otherActiveAdmins: 0)
            .Should().BeTrue();

    [Fact]
    public void Demoting_an_admin_is_allowed_while_another_one_survives()
        => AdminInvariantRules.CanReplaceRoles(
                targetIsActive: true, targetIsAdminNow: true, targetKeepsAdmin: false, otherActiveAdmins: 1)
            .Should().BeTrue();

    /// <summary>El caso que da nombre al issue: el lock-out por una edición de roles normal.</summary>
    [Fact]
    public void Demoting_the_last_active_admin_is_rejected()
        => AdminInvariantRules.CanReplaceRoles(
                targetIsActive: true, targetIsAdminNow: true, targetKeepsAdmin: false, otherActiveAdmins: 0)
            .Should().BeFalse();

    /// <summary>
    /// Un usuario inactivo no cuenta: no puede entrar a arreglar nada, así que quitarle el rol no
    /// reduce el número de administradores <i>activos</i>. Bloquearlo sería impedir una operación
    /// inofensiva — y dejaría sin poder limpiar los roles de cuentas ya desactivadas.
    /// </summary>
    [Fact]
    public void Demoting_an_inactive_admin_is_allowed_even_with_no_other_admins()
        => AdminInvariantRules.CanReplaceRoles(
                targetIsActive: false, targetIsAdminNow: true, targetKeepsAdmin: false, otherActiveAdmins: 0)
            .Should().BeTrue();

    /// <summary>
    /// Las dos rutas coinciden donde tienen que coincidir: con un admin activo y ningún otro, ni se
    /// puede desactivar ni se puede degradar. Si divergieran, la ruta laxa sería el agujero.
    /// </summary>
    [Fact]
    public void Both_paths_agree_on_the_last_active_admin()
    {
        AdminInvariantRules.CanDeactivate(true, 0).Should().BeFalse();
        AdminInvariantRules.CanReplaceRoles(true, true, false, 0).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(50)]
    public void Any_surviving_active_admin_is_enough(int survivors)
    {
        AdminInvariantRules.CanDeactivate(true, survivors).Should().BeTrue();
        AdminInvariantRules.CanReplaceRoles(true, true, false, survivors).Should().BeTrue();
    }
}
