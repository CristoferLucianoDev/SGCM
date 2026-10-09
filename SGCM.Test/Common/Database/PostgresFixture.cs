using Npgsql;
using Testcontainers.PostgreSql;

namespace SGCM.Test.Common.Database;

// Un contenedor para toda la corrida de pruebas; cada prueba limpia los datos con reset.sql.
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string OwnerPassword = "owner-test";
    private const string AppPassword = "app-test";
    private const string ReportingPassword = "reporting-test";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
    {
        Username = "sgcm_app",
        Password = AppPassword
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await _container.CopyAsync(new DirectoryInfo(RepositoryPaths.Database), "/database");
        await _container.CopyAsync(new DirectoryInfo(RepositoryPaths.TestDatabase), "/test-database");

        await RunScriptAsync(
            "/database/deploy.sql",
            "-v", $"owner_password={OwnerPassword}",
            "-v", $"app_password={AppPassword}",
            "-v", $"reporting_password={ReportingPassword}");
    }

    public Task ResetAsync() => RunScriptAsync("/test-database/reset.sql");

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

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