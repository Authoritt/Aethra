using System.Text.RegularExpressions;
using Aethra.Modules.Identity.Domain;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// El drill de la rama de bootstrap, en su versión barata.
///
/// <para>
/// La rama que concede ADMIN con la credencial de configuración estuvo abierta y muda durante toda
/// la vida del proyecto. Se le puso un aviso; el aviso, a su vez, no se había visto correr nunca.
/// Forzarlo de verdad parecía pedir un entorno donde vaciar la tabla de usuarios — pero el
/// disparador de esta rama <b>es un conteo de filas</b>, y un conteo de filas se falsifica
/// honestamente sin levantar nada. Así que los dos casos se ejercitan en cada build.
/// </para>
///
/// <para>
/// Lo que estos tests NO son: una prueba de que el host arranca, ni de que Serilog escribe. Cubren
/// la decisión y el contenido del mensaje. Lo que queda fuera —que <c>Program.cs</c> llame a esto—
/// lo sostiene el compilador, porque no hay otra fuente para estas plantillas.
/// </para>
/// </summary>
public class BootstrapLoginPostureTests
{
    [Theory]
    [InlineData(0, BootstrapLoginPosture.Abierto)]   // instalacion nueva, o sembrador que no corrio
    [InlineData(1, BootstrapLoginPosture.Cerrado)]   // el primer usuario ya cierra la rama
    [InlineData(2, BootstrapLoginPosture.Cerrado)]
    [InlineData(int.MaxValue, BootstrapLoginPosture.Cerrado)]
    public void La_postura_sale_del_conteo_de_usuarios(int usuarios, BootstrapLoginPosture esperada)
        => Assert.Equal(esperada, BootstrapLogin.Evaluar(usuarios));

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Un_conteo_absurdo_avisa_en_vez_de_callar(int usuarios)
    {
        // Un conteo negativo no significa "hay menos de cero usuarios": significa que el conteo no
        // es de fiar. Ante la duda se avisa, porque el coste de los dos errores no es simetrico.
        Assert.Equal(BootstrapLoginPosture.Abierto, BootstrapLogin.Evaluar(usuarios));
    }

    [Fact]
    public void El_aviso_nombra_lo_que_hay_que_tocar_para_arreglarlo()
    {
        // Un aviso que dice "postura insegura" y nada mas obliga a quien lo lee a investigar desde
        // cero. Estos cuatro datos son los que convierten la linea de log en una accion: que da
        // ADMIN, con que credencial, de que flag depende, y donde esta el hilo abierto.
        Assert.Contains("ADMIN", BootstrapLogin.PlantillaAbierto, StringComparison.Ordinal);
        Assert.Contains("Identity__AdminPasswordSeed", BootstrapLogin.PlantillaAbierto, StringComparison.Ordinal);
        Assert.Contains("ApplyMigrationsOnStart", BootstrapLogin.PlantillaAbierto, StringComparison.Ordinal);
        Assert.Contains("issue #21", BootstrapLogin.PlantillaAbierto, StringComparison.Ordinal);
    }

    [Fact]
    public void El_mensaje_de_cerrado_no_suena_a_alarma()
    {
        // Si el caso sano tambien mencionara la credencial, el log entrenaria a ignorarlo.
        Assert.DoesNotContain("ADMIN", BootstrapLogin.PlantillaCerrado, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity__AdminPasswordSeed", BootstrapLogin.PlantillaCerrado, StringComparison.Ordinal);
        Assert.Contains("NO es alcanzable", BootstrapLogin.PlantillaCerrado, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BootstrapLogin.PlantillaAbierto, BootstrapLogin.ArgumentosAbierto)]
    [InlineData(BootstrapLogin.PlantillaCerrado, BootstrapLogin.ArgumentosCerrado)]
    public void Cada_plantilla_pide_tantos_argumentos_como_declara(string plantilla, int esperados)
    {
        // Serilog NO falla cuando faltan argumentos: escribe el hueco literal ("{Aplicar}") y sigue.
        // Un mensaje asi solo se descubre leyendo el log de produccion, que es justo el momento en
        // que hace falta que se entienda. Aqui el desajuste rompe el build.
        Assert.Equal(esperados, Huecos(plantilla).Count);
    }

    [Fact]
    public void Los_huecos_van_en_el_orden_en_que_el_host_pasa_los_argumentos()
    {
        // Serilog casa plantilla y argumentos POR POSICION, no por nombre: invertir los dos huecos
        // publicaria el flag como numero de usuarios sin que nada se queje.
        Assert.Equal(["Usuarios", "Aplicar"], Huecos(BootstrapLogin.PlantillaAbierto));
        Assert.Equal(["Usuarios"], Huecos(BootstrapLogin.PlantillaCerrado));
    }

    private static List<string> Huecos(string plantilla)
        => Regex.Matches(plantilla, @"\{(\w+)\}")
            .Select(m => m.Groups[1].Value)
            .ToList();
}
