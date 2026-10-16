using SGCM.Test.Common.Database;

namespace SGCM.Test.Common.Web;

/// <summary>
/// App de pruebas única para toda la ejecución: un <see cref="SgcmApplicationFactory"/> contra
/// <see cref="SharedDatabase"/>, creado la primera vez que alguien lo pide y liberado al terminar el
/// proceso. Cada prueba o escenario crea su propio <see cref="HttpClient"/> y solo desecha ese cliente.
/// </summary>
/// <remarks>
/// Regla para el equipo: la app se comparte entre todas las pruebas de integración y los escenarios
/// BDD, así que quien registre un servicio con estado en memoria debe agregarlo a
/// <see cref="SgcmApplicationFactory.ResetState"/>. Si no, lo que deje una prueba lo ve la siguiente.
/// </remarks>
public static class SharedApp
{
    // El modo por defecto de Lazy garantiza que se cree una sola app aunque varias pruebas la pidan a la vez.
    private static readonly Lazy<Task<SgcmApplicationFactory>> Factory = new(CreateAsync);

    /// <summary>
    /// Devuelve la app compartida y la crea si nadie la pidió antes.
    /// </summary>
    /// <returns>La única instancia de la ejecución. No la deseches: se libera al terminar el proceso.</returns>
    public static Task<SgcmApplicationFactory> GetAsync() => Factory.Value;

    /// <summary>
    /// Indica si alguien ya pidió la app compartida, aunque su creación todavía no haya terminado.
    /// </summary>
    public static bool IsInitialized => Factory.IsValueCreated;

    /// <summary>
    /// Deja la base y la app compartidas como recién creadas: limpia la base con <c>reset.sql</c> y
    /// llama a <see cref="SgcmApplicationFactory.ResetState"/>.
    /// </summary>
    /// <remarks>
    /// Es el único punto de reinicio entre pruebas: lo llaman <see cref="ApiIntegrationTestBase"/> antes
    /// de cada prueba de integración y el hook de los escenarios BDD antes de cada escenario. Un paso de
    /// limpieza nuevo va aquí o en <see cref="SgcmApplicationFactory.ResetState"/>, nunca en quien lo llama.
    /// </remarks>
    /// <returns>La app compartida, ya reiniciada.</returns>
    public static async Task<SgcmApplicationFactory> ResetAsync()
    {
        var database = await SharedDatabase.GetAsync();
        await database.ResetAsync();

        var factory = await GetAsync();
        factory.ResetState();

        return factory;
    }

    private static async Task<SgcmApplicationFactory> CreateAsync()
    {
        var database = await SharedDatabase.GetAsync();
        var factory = new SgcmApplicationFactory(database.ConnectionString);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => factory.Dispose();

        return factory;
    }
}