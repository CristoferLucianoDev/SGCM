using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using SGCM.Web;
using System;
using ILogger = Serilog.ILogger;

namespace SGCM.Test.Common.Web;

/// <summary>
/// Levanta la app completa en memoria contra la base de Testcontainers, con el envío
/// de notificaciones reemplazado por <see cref="FakeNotificationService"/>.
/// </summary>
/// <param name="connectionString">Cadena de conexión de la base de pruebas.</param>
/// <param name="configurationOverrides">
/// Valores de configuración que pisan los de prueba, o <c>null</c> para usar solo esos.
/// </param>
public sealed class SgcmApplicationFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? configurationOverrides = null) : WebApplicationFactory<Program>
{
    public const string ConfirmationUrl = "https://sgcm.test/cuenta/confirmar";
    public const string PasswordResetUrl = "https://sgcm.test/cuenta/restablecer";

    private const int TestHashIterations = 1000;

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
                ["Accounts:PasswordResetUrl"] = PasswordResetUrl,
                // En el servidor de pruebas todas las peticiones llegan sin IP y comparten la
                // partición del límite por IP. La app se comparte en toda la ejecución y el rate
                // limiter de ASP.NET no se puede reiniciar, así que el límite alcanza para todas
                // las pruebas juntas; las pruebas de ese límite lo bajan en una app propia.
                ["LoginLimits:MaxRequestsPerIp"] = "100000",
                // Mismo motivo que el límite por IP del inicio de sesión.
                ["PasswordResetLimits:MaxRequestsPerIp"] = "100000",
                // Apagados para que no alteren los datos que prepara cada prueba.
                ["Worker:Enabled"] = "false"
            }));

        // Para pruebas que necesitan otra configuración, como un límite por IP bajo. Va después
        // de los valores de prueba porque la última fuente agregada es la que gana.
        if (configurationOverrides is not null)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(configurationOverrides));
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddSingleton<INotificationService>(Notifications);
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(Clock);
            // Las pruebas no necesitan el costo de producción, y el formato del hash no cambia:
            // V3 guarda las iteraciones dentro de cada hash.
            services.Configure<PasswordHasherOptions>(options => options.IterationCount = TestHashIterations);
            // Reemplazable para que ResetState vacíe los intentos sin tocar el limitador de producción.
            services.RemoveAll<IAttemptLimiter>();
            services.AddSingleton(provider => new ResettableAttemptLimiter(Clock, provider.GetRequiredService<ILogger>()));
            services.AddSingleton<IAttemptLimiter>(provider => provider.GetRequiredService<ResettableAttemptLimiter>());
        });
    }

    /// <summary>
    /// Deja la app como recién creada: el reloj en <see cref="UserMother.Now"/>, sin notificaciones
    /// guardadas y sin intentos en el limitador. Se llama antes de cada prueba de integración y de
    /// cada escenario BDD, junto con la limpieza de la base.
    /// </summary>
    /// <remarks>
    /// Regla para el equipo: la app se comparte entre todas las pruebas (ver <see cref="SharedApp"/>),
    /// así que quien registre un servicio con estado en memoria debe reiniciarlo aquí. El rate limiter
    /// de ASP.NET no se puede reiniciar; por eso la configuración de prueba sube sus límites por IP.
    /// </remarks>
    public void ResetState()
    {
        Clock.Reset();
        Notifications.Clear();
        Services.GetRequiredService<ResettableAttemptLimiter>().Clear();
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
    /// Tokens en claro de los correos de recuperación de contraseña enviados a
    /// <paramref name="email"/>, en orden de envío: es lo que el usuario recibe en el enlace del correo.
    /// </summary>
    /// <param name="email">Correo del destinatario.</param>
    /// <returns>Los tokens, del primero al último enviado; vacía si no se envió ninguno.</returns>
    public IReadOnlyList<string> PasswordResetTokensFor(string email) =>
        Notifications.Sent
            .OfType<PasswordResetNotification>()
            .Where(notification => notification.Recipient.Email == email)
            .Select(notification => QueryHelpers.ParseQuery(new Uri(notification.ResetUrl).Query)["token"].ToString())
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

    /// <summary>
    /// Carga el usuario, le aplica el cambio y lo guarda, sin disparar sus eventos.
    /// </summary>
    /// <param name="email">Correo del usuario.</param>
    /// <param name="change">Cambio que se aplica al agregado cargado.</param>
    /// <exception cref="InvalidOperationException">Si no hay un usuario con ese correo.</exception>
    public async Task UpdateUserAsync(string email, Action<User> change)
    {
        using var scope = Services.CreateScope();
        var user = await FindUserAsync(scope, email);

        change(user);
        user.ClearDomainEvents();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Calcula el hash de la contraseña con el <see cref="IPasswordHasher"/> de la app, para
    /// crear usuarios que puedan iniciar sesión.
    /// </summary>
    /// <param name="password">Contraseña en claro; debe cumplir la política de longitud.</param>
    /// <returns>El hash, listo para guardarlo en el usuario.</returns>
    public string HashPassword(string password) =>
        Services.GetRequiredService<IPasswordHasher>().Hash(Password.Create(password).Value);

    /// <summary>
    /// Crea el perfil de paciente del usuario con ese correo, con <see cref="TestDataSeeder"/>.
    /// </summary>
    /// <param name="email">Correo de un usuario con el rol de paciente.</param>
    /// <exception cref="InvalidOperationException">Si no hay un usuario con ese correo.</exception>
    public Task InsertPatientProfileAsync(string email) =>
        SeedAsync(email, TestDataSeeder.InsertPatientProfileAsync);

    /// <summary>
    /// Activa el segundo factor del usuario con ese correo, con <see cref="TestDataSeeder"/>.
    /// </summary>
    /// <param name="email">Correo del usuario.</param>
    /// <exception cref="InvalidOperationException">Si no hay un usuario con ese correo.</exception>
    public Task EnableTwoFactorAsync(string email) =>
        SeedAsync(email, (context, userId) => TestDataSeeder.EnableTwoFactorAsync(context, userId));

    private async Task SeedAsync(string email, Func<AppDbContext, int, Task> seed)
    {
        using var scope = Services.CreateScope();
        var user = await FindUserAsync(scope, email);

        await seed(scope.ServiceProvider.GetRequiredService<AppDbContext>(), user.Id);
    }

    private static async Task<User> FindUserAsync(IServiceScope scope, string email) =>
        await scope.ServiceProvider.GetRequiredService<IUserRepository>()
            .GetByEmailAsync(Email.Create(email).Value, CancellationToken.None)
        ?? throw new InvalidOperationException($"No hay un usuario con el correo {email}.");
}