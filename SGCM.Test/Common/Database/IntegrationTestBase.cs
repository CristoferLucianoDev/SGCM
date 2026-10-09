namespace SGCM.Test.Common.Database;

// Base de las pruebas de integración: deja la base limpia antes de cada prueba.
[Collection(DatabaseCollection.Name)]
public abstract class IntegrationTestBase(PostgresFixture database) : IAsyncLifetime
{
    protected PostgresFixture Database { get; } = database;

    public virtual Task InitializeAsync() => Database.ResetAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;
}