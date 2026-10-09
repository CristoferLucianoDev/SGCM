using Npgsql;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class DatabaseSetupTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task La_base_tiene_los_roles_semilla()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT count(*) FROM app.role", connection);
        var count = (long)(await command.ExecuteScalarAsync())!;

        count.ShouldBe(3);
    }

    [Fact]
    public async Task sgcm_app_puede_insertar_un_usuario()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """INSERT INTO app."user" (email, password_hash, role_id) VALUES ('prueba@correo.com', 'hash', 1) RETURNING id""",
            connection);
        var id = await command.ExecuteScalarAsync();

        id.ShouldNotBeNull();
    }
}