using Reqnroll;
using SGCM.Test.Common.Database;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Un contenedor PostgreSQL para todos los escenarios; cada escenario empieza con la base limpia.
/// </summary>
[Binding]
public sealed class AcceptanceHooks
{
    public static PostgresFixture Database { get; } = new();

    [BeforeTestRun]
    public static Task StartDatabaseAsync() => Database.InitializeAsync();

    [AfterTestRun]
    public static Task StopDatabaseAsync() => Database.DisposeAsync();

    [BeforeScenario]
    public static Task ResetDatabaseAsync() => Database.ResetAsync();
}