namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Si la rama de login por bootstrap está ABIERTA o no.
/// </summary>
public enum BootstrapLoginPosture
{
    /// <summary>Hay usuarios en BD: la rama de bootstrap NO es alcanzable.</summary>
    Cerrado,

    /// <summary>
    /// Cero usuarios: <c>/auth/login</c> concederá ADMIN con la credencial de configuración.
    /// Correcto en una instalación nueva; en cualquier otra significa que el sembrador no corrió
    /// y esa credencial sigue viva.
    /// </summary>
    Abierto,
}

/// <summary>
/// La decisión y los mensajes de la declaración de postura que el host emite al arrancar.
///
/// <para>
/// Vive aquí, y no como un <c>if</c> dentro de <c>Program.cs</c>, por un motivo concreto: una rama
/// que nada ejercita es indistinguible de código muerto, y el aviso que denuncia un fallback callado
/// no puede ser a su vez un fallback callado. El disparador de esta rama es <b>un conteo de filas</b>,
/// y un conteo de filas es barato de falsificar honestamente — así que los dos casos se ejercitan en
/// CADA build (<c>BootstrapLoginPostureTests</c>) en lugar de esperar a un entorno donde forzarlos.
/// </para>
///
/// <para>
/// El arreglo de fondo — que la credencial semilla deje de valer sola — sigue abierto en el issue #21.
/// Esto no lo arregla: hace que su estado quede declarado, y que la declaración esté probada.
/// </para>
/// </summary>
public static class BootstrapLogin
{
    /// <summary>
    /// Plantilla del caso ABIERTO. Es una plantilla de Serilog, no una cadena ya formateada, para que
    /// <c>Usuarios</c> y <c>Aplicar</c> lleguen al log como propiedades consultables y no como texto.
    /// Espera <see cref="ArgumentosAbierto"/> argumentos, en ese orden.
    /// </summary>
    public const string PlantillaAbierto =
        "ARRANQUE: {Usuarios} usuarios en BD, asi que /auth/login concedera ADMIN con la credencial "
        + "de configuracion (Identity__AdminPasswordSeed). Correcto en una instalacion nueva. "
        + "Si no lo es, el sembrador no corrio -- vive dentro de ApplyMigrationsOnStart, que "
        + "ahora vale {Aplicar} -- y esa credencial sigue viva. Ver issue #21.";

    /// <summary>Plantilla del caso CERRADO. Espera <see cref="ArgumentosCerrado"/> argumentos.</summary>
    public const string PlantillaCerrado =
        "ARRANQUE: {Usuarios} usuario(s) en BD; la rama de login por bootstrap NO es alcanzable.";

    /// <summary>
    /// Aridad esperada de cada plantilla. Serilog no falla cuando faltan argumentos: escribe el hueco
    /// tal cual (<c>{Aplicar}</c>) y sigue, así que un mensaje roto solo se descubre leyendo el log de
    /// producción — justo el sitio donde este mensaje tiene que ser legible. Declarada, se comprueba.
    /// </summary>
    public const int ArgumentosAbierto = 2;

    /// <inheritdoc cref="ArgumentosAbierto"/>
    public const int ArgumentosCerrado = 1;

    /// <summary>
    /// Un conteo negativo se trata como abierto: si el conteo es absurdo no se sabe nada, y ante la
    /// duda se avisa. Un aviso de más cuesta una línea de log; uno de menos cuesta el issue #21.
    /// </summary>
    public static BootstrapLoginPosture Evaluar(int usuarios)
        => usuarios > 0 ? BootstrapLoginPosture.Cerrado : BootstrapLoginPosture.Abierto;
}
