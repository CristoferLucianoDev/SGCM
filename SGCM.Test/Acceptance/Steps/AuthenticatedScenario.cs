using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Autenticación real de los escenarios administrativos: crea un usuario con el rol pedido y su
/// perfil, inicia sesión por la API y deja el access token en el cliente del escenario.
/// </summary>
internal static class AuthenticatedScenario
{
    private const string Password = "Secreta123";
    private const string UserAgent = "PruebasSGCM/1.0";

    /// <summary>
    /// Inicia sesión con un usuario nuevo del rol indicado, sin paso pendiente.
    /// </summary>
    /// <param name="scenario">Escenario cuyo cliente queda autenticado.</param>
    /// <param name="role">Nombre del rol, tal como lo escribe el escenario.</param>
    /// <param name="emailPrefix">Prefijo del correo del usuario, para reconocer quién lo creó.</param>
    /// <param name="onUserSaved">
    /// Se llama con el id del usuario apenas se guarda, para que quien lo pidió pueda limpiarlo
    /// aunque falle un paso posterior.
    /// </param>
    /// <returns>Una tarea que termina cuando el cliente tiene el header Authorization.</returns>
    public static async Task LoginAsync(ApiScenario scenario, string role, string emailPrefix, Action<int>? onUserSaved = null)
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@correo.com";
        scenario.Factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        var user = UserMother.WithRole(Enum.Parse<UserRole>(role), email: Email.Create(email).Value,
            passwordHash: scenario.Factory.HashPassword(Password));
        await scenario.Factory.SaveAsync(user);
        onUserSaved?.Invoke(user.Id);
        using (var scope = scenario.Factory.Services.CreateScope())
        {
            await TestDataSeeder.InsertProfileAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), user);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = Password })
        };
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await scenario.Client.SendAsync(request);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        login.AccessToken.ShouldNotBeNullOrEmpty();
        login.PendingStep.ShouldBeNull();
        var jwt = new JsonWebToken(login.AccessToken);
        jwt.Claims.Where(claim => claim.Type == AppClaims.Role).Select(claim => claim.Value)
            .ShouldBe(new[] { role });
        jwt.Claims.ShouldNotContain(claim => claim.Type == AppClaims.PendingStep);
        scenario.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
}
