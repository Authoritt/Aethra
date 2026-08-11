namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Decide si una operación sobre un usuario dejaría a la instalación <b>sin ningún administrador
/// activo</b>. Función pura: no consulta la base, recibe los hechos ya medidos.
///
/// <para>Existe porque el invariante se aplicaba en <b>un solo</b> camino del ciclo de vida
/// (desactivar) y no en el otro (reemplazar roles), de modo que una edición de roles perfectamente
/// normal podía dejar el sistema sin administradores y exigir recuperación por base de datos. Un
/// invariante que solo se comprueba en algunas de las rutas que pueden violarlo, no es un
/// invariante.</para>
///
/// <para>Está aquí, y no dentro de cada handler, por dos razones: para que ambas rutas compartan
/// literalmente la misma decisión, y para que esa decisión se pueda probar sin base de datos —el
/// mismo patrón que el repo ya usa en <c>NativeRolloutPlanner</c> o <c>ContainerHealthRules</c>.</para>
/// </summary>
public static class AdminInvariantRules
{
    /// <summary>Código de error único para las dos rutas, para que el cliente no distinga.</summary>
    public const string LastAdminErrorCode = "user.last_admin";

    /// <summary>
    /// ¿Se puede desactivar a este usuario?
    /// </summary>
    /// <param name="targetIsAdmin">Si el usuario que se va a desactivar tiene hoy el rol admin.</param>
    /// <param name="otherActiveAdmins">
    /// Cuántos OTROS usuarios activos tienen el rol admin (excluido el propio objetivo).
    /// </param>
    /// <returns><c>true</c> si la operación es segura.</returns>
    public static bool CanDeactivate(bool targetIsAdmin, int otherActiveAdmins)
        => !targetIsAdmin || otherActiveAdmins > 0;

    /// <summary>
    /// ¿Se puede reemplazar el juego de roles de este usuario?
    ///
    /// <para>Solo importa el caso en que el usuario <b>pierde</b> el rol admin: darlo, o mantenerlo,
    /// nunca reduce el número de administradores. Un usuario inactivo tampoco cuenta, porque no
    /// puede entrar a arreglar nada — quitarle el rol no cambia el número de admins <i>activos</i>.</para>
    /// </summary>
    /// <param name="targetIsActive">Si el usuario está activo.</param>
    /// <param name="targetIsAdminNow">Si tiene el rol admin ANTES del cambio.</param>
    /// <param name="targetKeepsAdmin">Si lo conservará DESPUÉS del cambio.</param>
    /// <param name="otherActiveAdmins">Cuántos OTROS usuarios activos tienen el rol admin.</param>
    /// <returns><c>true</c> si la operación es segura.</returns>
    public static bool CanReplaceRoles(
        bool targetIsActive, bool targetIsAdminNow, bool targetKeepsAdmin, int otherActiveAdmins)
    {
        var losesAdmin = targetIsActive && targetIsAdminNow && !targetKeepsAdmin;
        return !losesAdmin || otherActiveAdmins > 0;
    }
}
