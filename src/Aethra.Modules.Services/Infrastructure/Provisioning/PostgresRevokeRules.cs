namespace Aethra.Modules.Services.Infrastructure.Provisioning;

internal enum PostgresRevokeStep
{
    RevokeDatabasePrivileges,
    RestoreDatabaseOwner,
    OpenTargetDatabase,
    ReassignOwnedObjects,
    DropOwnedObjects,
    DropRole,
}

internal enum PostgresRevokeErrorDecision
{
    Fatal,
    BenignIdempotent,
}

internal static class PostgresRevokeRules
{
    public const string UndefinedObject = "42704";
    public const string InvalidCatalogName = "3D000";

    public static PostgresRevokeErrorDecision Classify(PostgresRevokeStep step, string? sqlState)
        => IsBenignIdempotent(step, sqlState)
            ? PostgresRevokeErrorDecision.BenignIdempotent
            : PostgresRevokeErrorDecision.Fatal;

    public static bool IsBenignIdempotent(PostgresRevokeStep step, string? sqlState)
        => step switch
        {
            // The role is already gone, which is the desired credential-removal state.
            PostgresRevokeStep.RevokeDatabasePrivileges => sqlState is UndefinedObject or InvalidCatalogName,
            // The binding database is already gone, so there is no database owner to restore.
            PostgresRevokeStep.RestoreDatabaseOwner => sqlState == InvalidCatalogName,
            // The binding database is already gone; continue with role removal in postgres.
            PostgresRevokeStep.OpenTargetDatabase => sqlState == InvalidCatalogName,
            // The role is already gone, so it cannot own objects in the target database.
            PostgresRevokeStep.ReassignOwnedObjects => sqlState == UndefinedObject,
            // The role is already gone, so there is nothing left to drop in this database.
            PostgresRevokeStep.DropOwnedObjects => sqlState == UndefinedObject,
            // DROP ROLE IF EXISTS should make absence non-error; keep this only as defense.
            PostgresRevokeStep.DropRole => sqlState == UndefinedObject,
            _ => false,
        };
}

internal sealed record PostgresRevokePlan(
    string DatabaseName,
    string Username,
    string AdminUsername,
    string DatabaseIdentifier,
    string UserIdentifier,
    string AdminIdentifier)
{
    public static PostgresRevokePlan Create(string resourceName, BindingCredentials credentials, AdminCredentials admin)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(admin);

        return new PostgresRevokePlan(
            resourceName,
            credentials.Username,
            admin.Username,
            PostgresIdentifier.Quote(resourceName),
            PostgresIdentifier.Quote(credentials.Username),
            // CURRENT_USER, no el nombre citado del admin. La sesion YA esta autenticada como ese
            // rol, asi que es exactamente el destinatario correcto de la propiedad — y ademas evita
            // un fallo real: PostgresIdentifier.Quote impone el alfabeto de los identificadores que
            // NOSOTROS generamos, pero el admin de un servicio ADOPTADO lo eligio otro y puede llevar
            // '-' o '@', que Postgres acepta perfectamente entre comillas. Con el nombre citado, un
            // servicio adoptado con un rol asi podia provisionar bindings pero no revocarlos NUNCA:
            // la credencial se quedaba activa para siempre.
            "CURRENT_USER");
    }
}
