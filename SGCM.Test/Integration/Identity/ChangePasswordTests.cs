using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using Shouldly;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class ChangePasswordTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string ChangePasswordUrl = "/api/auth/change-password";
    private const string RefreshUrl = "/api/auth/refresh";
    private const string Email = "paciente@correo.com";
    private const string CurrentPassword = "Secreta123";
    private const string NewPassword = "NuevaClave456";

    [Fact]
    public async Task El_paciente_cambia_su_contraseña_conserva_la_sesion_actual_y_se_revocan_las_demas()
    {
        var (current, other) = await LoginPatientInTwoDevicesAsync();
        Authorize(current.AccessToken);

        using var response = await ChangePasswordAsync(CurrentPassword, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        using (var otherRefresh = await RefreshAsync(other.RefreshToken))
            await otherRefresh.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        using (var currentRefresh = await RefreshAsync(current.RefreshToken))
            await currentRefresh.ShouldHaveStatusAsync(HttpStatusCode.OK);
        using (var oldLogin = await LoginAsync(Email, CurrentPassword))
            await oldLogin.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await LoginSuccessfullyAsync(Email, NewPassword);
    }

    [Fact]
    public async Task La_sesion_actual_sigue_autorizada_despues_del_cambio()
    {
        var (current, _) = await LoginPatientInTwoDevicesAsync();
        Authorize(current.AccessToken);
        using (var changed = await ChangePasswordAsync(CurrentPassword, NewPassword))
            await changed.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        // Con el mismo access token se puede volver a cambiar, así que la sesión no se cerró.
        using var again = await ChangePasswordAsync(NewPassword, "OtraClave789");

        await again.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await LoginSuccessfullyAsync(Email, "OtraClave789");
    }

    [Fact]
    public async Task Una_contraseña_actual_incorrecta_responde_400_y_no_cambia_nada()
    {
        var (current, other) = await LoginPatientInTwoDevicesAsync();
        Authorize(current.AccessToken);

        using var response = await ChangePasswordAsync("Equivocada1", NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.CurrentPasswordIncorrect.Code);
        using (var otherRefresh = await RefreshAsync(other.RefreshToken))
            await otherRefresh.ShouldHaveStatusAsync(HttpStatusCode.OK);
        await LoginSuccessfullyAsync(Email, CurrentPassword);
    }

    [Theory]
    [InlineData("", NewPassword, "User.CurrentPasswordRequired")]
    [InlineData(CurrentPassword, "Corta12", "User.PasswordTooShort")]
    [InlineData(CurrentPassword, CurrentPassword, "User.NewPasswordSameAsCurrent")]
    public async Task Datos_invalidos_responden_400_y_no_cambian_la_contraseña(
        string current, string password, string errorCode)
    {
        var (tokens, _) = await LoginPatientInTwoDevicesAsync();
        Authorize(tokens.AccessToken);

        using var response = await ChangePasswordAsync(current, password);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(errorCode);
        await LoginSuccessfullyAsync(Email, CurrentPassword);
    }

    [Fact]
    public async Task Se_acepta_una_contraseña_nueva_de_8_caracteres()
    {
        var (tokens, _) = await LoginPatientInTwoDevicesAsync();
        Authorize(tokens.AccessToken);
        var password = new string('A', 8);

        using var response = await ChangePasswordAsync(CurrentPassword, password);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await LoginSuccessfullyAsync(Email, password);
    }

    [Fact]
    public async Task Un_paciente_con_el_perfil_pendiente_recibe_403()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await CreateConfirmedPatientAsync(Email, CurrentPassword);
        var login = await LoginSuccessfullyAsync(Email, CurrentPassword);
        login.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
        Authorize(login.AccessToken);

        using var response = await ChangePasswordAsync(CurrentPassword, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await LoginSuccessfullyAsync(Email, CurrentPassword);
    }

    [Theory]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public async Task Otros_roles_responden_403(UserRole role)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var token = Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(1, role, 1, null);
        Authorize(token.Value);

        using var response = await ChangePasswordAsync(CurrentPassword, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var response = await ChangePasswordAsync(CurrentPassword, NewPassword);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    // Paciente con perfil completo y dos sesiones activas: la actual y la de otro dispositivo.
    // Los access tokens vencen según el reloj real, así que la app emite con la hora actual.
    private async Task<(LoginResponse Current, LoginResponse Other)> LoginPatientInTwoDevicesAsync()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        await CreateConfirmedPatientAsync(Email, CurrentPassword);
        await Factory.InsertPatientProfileAsync(Email);

        var other = await LoginSuccessfullyAsync(Email, CurrentPassword);
        var current = await LoginSuccessfullyAsync(Email, CurrentPassword);
        current.PendingStep.ShouldBeNull();

        return (current, other);
    }

    private void Authorize(string? accessToken) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private Task<HttpResponseMessage> ChangePasswordAsync(string currentPassword, string newPassword) =>
        Client.PostAsJsonAsync(ChangePasswordUrl, new ChangePasswordRequest(currentPassword, newPassword));

    private Task<HttpResponseMessage> RefreshAsync(string? refreshToken) =>
        Client.PostAsJsonAsync(RefreshUrl, new RefreshSessionRequest(refreshToken!));
}
