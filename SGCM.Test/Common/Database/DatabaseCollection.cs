namespace SGCM.Test.Common.Database;

// Todas las pruebas de esta colección comparten un solo contenedor y no corren en paralelo entre sí.
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Database";
}