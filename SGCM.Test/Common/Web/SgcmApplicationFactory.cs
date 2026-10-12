using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Fakes;
using SGCM.Web;
using System;

namespace SGCM.Test.Common.Web;

/// <summary>
/// Levanta la app completa en memoria contra la base de Testcontainers, con el envío
/// de notificaciones reemplazado por <see cref="FakeNotificationService"/>.
/// </summary>
public sealed class SgcmApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string ConfirmationUrl = "https://sgcm.test/cuenta/confirmar";

    public FakeNotificationService Notifications { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development activa ValidateOnBuild: si falta un registro, la prueba falla al arrancar.
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:SgcmDatabase"] = connectionString,
                ["Jwt:SigningKey"] = new string('k', JwtOptions.MinimumSigningKeyBytes),
                ["Accounts:EmailConfirmationUrl"] = ConfirmationUrl,
                // Apagados para que no alteren los datos que prepara cada prueba.
                ["Worker:Enabled"] = "false"
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddSingleton<INotificationService>(Notifications);
        });
    }

    /// <summary>
    /// Lee el usuario con sus tokens tal como quedó en la base.
    /// </summary>
    public async Task<User?> FindUserAsync(string email)
    {
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        return await repository.GetByEmailAsync(Email.Create(email).Value, CancellationToken.None);
    }

    /// <summary>
    /// Guarda un usuario preparado por la prueba, sin disparar sus eventos.
    /// </summary>
    public async Task SaveAsync(User user)
    {
        user.ClearDomainEvents();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}