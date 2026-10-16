using Reqnroll;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Inicia la base y la app compartidas con las pruebas de integración; cada escenario empieza con la
/// base limpia y la app compartida reiniciada.
/// </summary>
[Binding]
public sealed class AcceptanceHooks
{
    // Crear la app también inicia la base compartida.
    [BeforeTestRun]
    public static async Task StartSharedAppAsync() => await SharedApp.GetAsync();

    // Solo para @Integration: esos escenarios no corren en paralelo con nada. Un escenario sin la
    // etiqueta podría correr junto a DatabaseCollection y limpiarle la base a mitad de una prueba.
    [BeforeScenario("Integration")]
    public static Task ResetStateAsync() => SharedApp.ResetAsync();
}