namespace SGCM.Test.Common.Database;

/// <summary>
/// Fixture de <see cref="DatabaseCollection"/>. xUnit v2 crea su propia instancia y no deja que una
/// fixture reciba otra por constructor, así que delega en <see cref="SharedDatabase"/> para usar el
/// mismo contenedor que los escenarios BDD; cada prueba limpia los datos con reset.sql.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private SharedDatabase? _database;

    /// <summary>
    /// Cadena de conexión con el usuario de la aplicación.
    /// </summary>
    public string ConnectionString => Database.ConnectionString;

    private SharedDatabase Database =>
        _database ?? throw new InvalidOperationException("La base compartida no se inició.");

    /// <summary>
    /// Inicia la base compartida si nadie lo hizo antes y la deja lista para la colección.
    /// </summary>
    /// <returns>Una tarea que termina cuando la base está desplegada.</returns>
    public async Task InitializeAsync() => _database = await SharedDatabase.GetAsync();

    /// <summary>
    /// Borra los datos para que la siguiente prueba empiece con la base limpia.
    /// </summary>
    /// <returns>Una tarea que termina cuando la base quedó limpia.</returns>
    public Task ResetAsync() => Database.ResetAsync();

    /// <summary>
    /// No libera nada: el contenedor dura toda la ejecución y lo libera <see cref="SharedDatabase"/>
    /// al terminar el proceso.
    /// </summary>
    /// <returns>Una tarea ya completada.</returns>
    public Task DisposeAsync() => Task.CompletedTask;
}