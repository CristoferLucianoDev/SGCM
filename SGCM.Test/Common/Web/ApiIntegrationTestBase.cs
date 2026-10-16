using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Test.Common.Web;

/// <summary>
/// Base de las pruebas de la API: base limpia, la app compartida reiniciada y un cliente propio por prueba.
/// </summary>
public abstract class ApiIntegrationTestBase(PostgresFixture database) : IntegrationTestBase(database)
{
    /// <summary>
    /// User-Agent que envían los inicios de sesión; la sesión lo guarda como nombre del dispositivo.
    /// </summary>
    protected const string UserAgent = "PruebasSGCM/1.0";

    private const string RegisterUrl = "/api/accounts/register";
    private const string ConfirmUrl = "/api/accounts/confirm-email";
    private const string LoginUrl = "/api/auth/login";
    private const string ForgotPasswordUrl = "/api/accounts/forgot-password";
    private const string ResetPasswordUrl = "/api/accounts/reset-password";

    private SgcmApplicationFactory? _factory;

    protected SgcmApplicationFactory Factory =>
        _factory ?? throw new InvalidOperationException("La app de pruebas no se inicializó.");

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Reloj de la app, que la prueba adelanta para simular el paso del tiempo.
    /// </summary>
    protected FakeDateTimeProvider Clock => Factory.Clock;

    // No llama a base.InitializeAsync: SharedApp.ResetAsync ya limpia la base y es el único punto
    // de reinicio entre pruebas.
    public override async Task InitializeAsync()
    {
        _factory = await SharedApp.ResetAsync();
        Client = _factory.CreateClient();
    }

    // La app es compartida con las demás pruebas: solo se desecha el cliente de esta.
    public override async Task DisposeAsync()
    {
        Client.Dispose();
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

    /// <summary>
    /// Registra un paciente por la API y verifica que responda 202.
    /// </summary>
    /// <param name="email">Correo del paciente.</param>
    /// <param name="password">Contraseña en claro.</param>
    /// <returns>Una tarea que termina cuando la cuenta quedó pendiente de confirmar.</returns>
    protected async Task RegisterPatientAsync(string email, string password)
    {
        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email, password });
        await response.ShouldHaveStatusAsync(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Registra un paciente por la API y confirma su correo con el último enlace recibido,
    /// con el mismo recorrido que haría el usuario.
    /// </summary>
    /// <param name="email">Correo del paciente.</param>
    /// <param name="password">Contraseña en claro.</param>
    /// <returns>Una tarea que termina cuando la confirmación respondió 204.</returns>
    protected async Task CreateConfirmedPatientAsync(string email, string password)
    {
        await RegisterPatientAsync(email, password);

        var confirm = await Client.PostAsJsonAsync(ConfirmUrl, new { token = Factory.ConfirmationTokensFor(email)[^1] });
        await confirm.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Guarda un usuario confirmado con el rol indicado y una contraseña con la que puede
    /// iniciar sesión, para los roles que no se registran por la API.
    /// </summary>
    /// <param name="role">Rol del usuario.</param>
    /// <param name="email">Correo del usuario.</param>
    /// <param name="password">Contraseña en claro; debe cumplir la política de longitud.</param>
    /// <param name="mustChangePassword">Si la contraseña es temporal y debe cambiarse.</param>
    /// <returns>Una tarea que termina cuando el usuario queda guardado.</returns>
    protected Task SaveUserAsync(UserRole role, string email, string password, bool mustChangePassword) =>
        SaveAsync(UserMother.WithRole(role, mustChangePassword, Email.Create(email).Value, Factory.HashPassword(password)));

    /// <summary>
    /// Envía un inicio de sesión con <see cref="UserAgent"/>, sin verificar la respuesta.
    /// </summary>
    /// <param name="email">Correo del usuario.</param>
    /// <param name="password">Contraseña en claro.</param>
    /// <param name="client">Cliente de otra app de pruebas, o <c>null</c> para usar <see cref="Client"/>.</param>
    /// <returns>La respuesta de la API.</returns>
    protected async Task<HttpResponseMessage> LoginAsync(string email, string password, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.UserAgent.ParseAdd(UserAgent);

        return await (client ?? Client).SendAsync(request);
    }

    /// <summary>
    /// Inicia sesión, verifica que responda 200 y lee el cuerpo.
    /// </summary>
    /// <param name="email">Correo del usuario.</param>
    /// <param name="password">Contraseña en claro.</param>
    /// <returns>La respuesta del inicio de sesión.</returns>
    protected async Task<LoginResponse> LoginSuccessfullyAsync(string email, string password)
    {
        var response = await LoginAsync(email, password);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return await response.ReadLoginAsync();
    }

    /// <summary>
    /// Pide el enlace de recuperación de contraseña, sin verificar la respuesta.
    /// </summary>
    /// <param name="email">Correo tal como lo escribiría el usuario.</param>
    /// <param name="client">Cliente de otra app de pruebas, o <c>null</c> para usar <see cref="Client"/>.</param>
    /// <returns>La respuesta de la API.</returns>
    protected Task<HttpResponseMessage> ForgotPasswordAsync(string email, HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync(ForgotPasswordUrl, new { email });

    /// <summary>
    /// Restablece la contraseña con el token del enlace, sin verificar la respuesta.
    /// </summary>
    /// <param name="token">Token en claro del enlace de recuperación.</param>
    /// <param name="newPassword">Contraseña nueva en claro.</param>
    /// <returns>La respuesta de la API.</returns>
    protected Task<HttpResponseMessage> ResetPasswordAsync(string token, string newPassword) =>
        Client.PostAsJsonAsync(ResetPasswordUrl, new { token, newPassword });
}