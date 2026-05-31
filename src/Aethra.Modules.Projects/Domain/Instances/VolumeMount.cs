namespace Aethra.Modules.Projects.Domain.Instances;

/// <summary>
/// Montaje de volumen para una <see cref="Instance"/>.
///
/// <see cref="Name"/>: nombre lógico del volumen Docker. La fábrica
/// <see cref="Instance"/> lo prefija con <c>{template.slug}-{client.slug}</c> automáticamente
/// para garantizar aislamiento entre tenants en la misma VM.
/// <see cref="ContainerPath"/>: punto de montaje dentro del contenedor.
/// <see cref="ReadOnly"/>: si <c>true</c>, montaje como <c>ro</c>.
/// </summary>
/// <remarks>
/// Sealed record (no record struct): se persiste como JSON column en la <see cref="Instance"/>.
/// </remarks>
public sealed record VolumeMount(string Name, string ContainerPath, bool ReadOnly = false);
