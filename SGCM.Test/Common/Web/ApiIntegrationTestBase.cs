using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Fakes;

namespace SGCM.Test.Common.Web;

/// <summary>
/// Base de las pruebas de la API: base limpia y una app nueva por prueba.
/// </summary>
public abstract class ApiIntegrationTestBase(PostgresFixture database) : IntegrationTestBase(database)
{
    private SgcmApplicationFactory? _factory;

    protected SgcmApplicationFactory Factory =>
        _factory ?? throw new InvalidOperationException("La app de pruebas no se inicializó.");

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Reloj de la app, que la prueba adelanta para simular el paso del tiempo.
    /// </summary>
    protected FakeDateTimeProvider Clock => Factory.Clock;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _factory = new SgcmApplicationFactory(Database.ConnectionString);
        Client = _factory.CreateClient();
    }

    public override async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>
    /// Lee el usuario con sus tokens tal como quedó en la base.
    /// </summary>
    protected Task<User?> FindUserAsync(string email) => Factory.FindUserAsync(email);

    /// <summary>
    /// Guarda un usuario preparado por la prueba, sin disparar sus eventos.
    /// </summary>
    protected Task SaveAsync(User user) => Factory.SaveAsync(user);
}