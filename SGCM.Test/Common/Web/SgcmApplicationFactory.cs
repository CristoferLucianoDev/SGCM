using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
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

    /// <summary>
    /// Reloj de la app. Empieza en <see cref="UserMother.Now"/> y la prueba lo adelanta
    /// para simular el paso del tiempo.
    /// </summary>
    public FakeDateTimeProvider Clock { get; } = new();

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
                // En el servidor de pruebas todas las peticiones llegan sin IP y comparten la
                // partición del límite por IP; las pruebas de ese límite lo bajan explícitamente.
                ["LoginLimits:MaxRequestsPerIp"] = "1000",
                // Apagados para que no alteren los datos que prepara cada prueba.
                ["Worker:Enabled"] = "false"
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddSingleton<INotificationService>(Notifications);
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(Clock);
        });
    }

    /// <summary>
    /// Tokens en claro de los correos de confirmación enviados a <paramref name="email"/>,
    /// en orden de envío: es lo que el usuario recibe en el enlace del correo.
    /// </summary>
    /// <param name="email">Correo del destinatario.</param>
    /// <returns>Los tokens, del primero al último enviado.</returns>
    public IReadOnlyList<string> ConfirmationTokensFor(string email) =>
        Notifications.Sent
            .OfType<AccountConfirmationNotification>()
            .Where(notification => notification.Recipient.Email == email)
            .Select(notification => QueryHelpers.ParseQuery(new Uri(notification.ConfirmationUrl).Query)["token"].ToString())
            .ToList();

    /// <summary>
    /// Devuelve la instancia del proceso automático que registró la app, para ejecutarlo
    /// a mano con todo su cableado real: scope, decoradores, handler y repositorio.
    /// </summary>
    /// <typeparam name="TJob">Tipo del proceso automático.</typeparam>
    /// <returns>La única instancia registrada de <typeparamref name="TJob"/>.</returns>
    public TJob GetJob<TJob>() where TJob : IHostedService =>
        Services.GetServices<IHostedService>().OfType<TJob>().Single();

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