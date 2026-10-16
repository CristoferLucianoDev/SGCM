using Npgsql;
using Testcontainers.PostgreSql;

namespace SGCM.Test.Common.Database;

/// <summary>
/// Base de datos única para toda la ejecución de pruebas: un contenedor de PostgreSQL con
/// <c>deploy.sql</c> aplicado, que se inicia la primera vez que alguien lo pide y se libera al
/// terminar el proceso. La usan <see cref="PostgresFixture"/> y los escenarios BDD.
/// </summary>
/// <remarks>
/// Compartirla solo es seguro porque nunca hay dos pruebas usándola a la vez, y eso depende de dos
/// reglas: todas las clases de integración están en <see cref="DatabaseCollection"/>, cuyas pruebas
/// xUnit no corre en paralelo entre sí, y todos los features que tocan la base llevan
/// <c>@Integration</c>, que <c>reqnroll.json</c> declara en <c>addNonParallelizableMarkerForTags</c>
/// para que esos escenarios corran solos, sin otras colecciones en paralelo.
/// </remarks>
public sealed class SharedDatabase
{
    private const string OwnerPassword = "owner-test";
    private const string AppPassword = "app-test";
    private const string ReportingPassword = "reporting-test";

    private static readonly SharedDatabase Instance = new();

    // El modo por defecto de Lazy garantiza un solo arranque aunque varias pruebas lo pidan a la vez.
    private static readonly Lazy<Task> Started = new(() => Instance.StartAsync());

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    private SharedDatabase()
    {
    }

    /// <summary>
    /// Cadena de conexión con el usuario de la aplicación.
    /// </summary>
    public string ConnectionString => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
    {
        Username = "sgcm_app",
        Password = AppPassword
    }.ConnectionString;

    /// <summary>
    /// Devuelve la base compartida y la inicia si nadie la pidió antes.
    /// </summary>
    /// <returns>La única instancia de la ejecución, con el esquema ya desplegado.</returns>
    /// <exception cref="InvalidOperationException">Si falla <c>deploy.sql</c>.</exception>
    public static async Task<SharedDatabase> GetAsync()
    {
        await Started.Value;
        return Instance;
    }

    /// <summary>
    /// Borra los datos con <c>reset.sql</c> para que la siguiente prueba empiece con la base limpia.
    /// </summary>
    /// <returns>Una tarea que termina cuando la base quedó limpia.</returns>
    /// <exception cref="InvalidOperationException">Si falla el script.</exception>
    public Task ResetAsync() => RunScriptAsync("/test-database/reset.sql");

    private async Task StartAsync()
    {
        // No hace falta un orden de cierre estricto: si el proceso muere antes, Testcontainers
        // limpia el contenedor por su cuenta.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _container.DisposeAsync().AsTask().GetAwaiter().GetResult();

        await _container.StartAsync();
        await _container.CopyAsync(new DirectoryInfo(RepositoryPaths.Database), "/database");
        await _container.CopyAsync(new DirectoryInfo(RepositoryPaths.TestDatabase), "/test-database");

        await RunScriptAsync(
            "/database/deploy.sql",
            "-v", $"owner_password={OwnerPassword}",
            "-v", $"app_password={AppPassword}",
            "-v", $"reporting_password={ReportingPassword}");
    }

    private async Task RunScriptAsync(string path, params string[] variables)
    {
        string[] command = ["psql", "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", "postgres", .. variables, "-f", path];

        var result = await _container.ExecAsync(command);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{path} falló: {result.Stderr}");
        }
    }
}