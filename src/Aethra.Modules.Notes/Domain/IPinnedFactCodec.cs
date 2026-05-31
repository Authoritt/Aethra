namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Codec simétrico para los valores de <see cref="PinnedFact"/>. La implementación viva en
/// Infrastructure (DataProtection) — aquí solo el contrato para mantener el dominio libre de
/// dependencias de infraestructura (regla DomainPurityTests).
/// </summary>
public interface IPinnedFactCodec
{
    byte[] Encode(string plainValue);

    string Decode(byte[] cipher);
}
