namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// Resuelve el conjunto efectivo de variables de entorno <b>runtime</b> de un Instance, aplicando
/// la cascada de scopes Project → Template → Client → Instance (lo más específico gana) y
/// fusionando las env vars planas con los secretos <b>descifrados</b>.
///
/// <para>
/// Es el único punto donde los secretos se devuelven en claro, y solo para alimentarlos
/// directamente al satélite al arrancar el contenedor (ver el orquestador de deployment). El
/// resultado no debe loguearse ni persistirse.
/// </para>
///
/// <para>
/// Precedencia exacta (orden de aplicación, el último gana): por cada scope de menos a más
/// específico se aplican primero las env vars y luego los secretos de ese scope. Es decir, dentro
/// de un mismo scope un secreto pisa una env var con la misma key, y cualquier valor de un scope
/// más específico pisa al de uno más general.
/// </para>
/// </summary>
public interface IEnvironmentResolver
{
    /// <summary>
    /// Devuelve el diccionario fusionado de env vars runtime + secretos descifrados para el
    /// Instance descrito por <paramref name="scope"/>. Los IDs vacíos en la cadena se ignoran.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ResolveRuntimeEnvAsync(
        EnvironmentScopeChain scope, CancellationToken ct);
}

/// <summary>
/// Cadena de IDs de scope para resolver la cascada de un Instance. Cada ID es el identificador
/// textual del aggregate (<c>prj_*</c>, <c>tpl_*</c>, <c>cli_*</c>, <c>ins_*</c>).
/// </summary>
public sealed record EnvironmentScopeChain(
    string ProjectId,
    string TemplateId,
    string ClientId,
    string InstanceId);
