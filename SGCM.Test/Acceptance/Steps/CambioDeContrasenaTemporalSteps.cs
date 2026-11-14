using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Common;
using SGCM.Web.Contracts.Identity;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class CambioDeContrasenaTemporalSteps(ApiScenario scenario, InicioDeSesionSteps inicio)
{
    private const string Email = "medico205@correo.com";
    private const string Temporal = "Temporal205";
    private const string Nueva = "Nueva205";

    private LoginResponse _login = null!;
    private LoginResponse _cambio = null!;
    private int _sesionMovilId;
    private int _doctorId;
    private int _especialidadInicialId;
    private int _especialidadObjetivoId;
    private string[] _perfilAntesDeAsociar = [];
    private string[] _asociacionesAntesDeAsociar = [];

    [Given("que tengo un perfil médico, una contraseña temporal y una especialidad activa disponible para asociar")]
    public async Task PrepararMedicoConPerfil()
    {
        await PrepararMedicoSinPerfil();
        var user = (await scenario.Factory.FindUserAsync(Email)).ShouldNotBeNull();
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inicial = Specialty.Create("Especialidad inicial 205", "Ya asociada al médico.").Value;
        var objetivo = Specialty.Create("Especialidad disponible 205", "Disponible para asociar.").Value;
        context.Specialties.AddRange(inicial, objetivo);
        await context.SaveChangesAsync();

        var ubicacion = await context.Database.SqlQuery<Ubicacion>(
            $"""
            SELECT province_id AS provincia_id, id AS municipio_id
            FROM people.municipality ORDER BY id LIMIT 1
            """).SingleAsync();
        var doctor = DoctorMother.Create([inicial.Id], userId: user.Id,
            provinceId: ubicacion.ProvinciaId, municipalityId: ubicacion.MunicipioId).Value;
        context.Doctors.Add(doctor);
        await context.SaveChangesAsync();

        _doctorId = doctor.Id;
        _especialidadInicialId = inicial.Id;
        _especialidadObjetivoId = objetivo.Id;
        _perfilAntesDeAsociar = await TestDataSeeder.ReadDoctorsAsync(context, [_doctorId]);
        _asociacionesAntesDeAsociar = await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, [_doctorId]);
        _asociacionesAntesDeAsociar.ShouldHaveSingleItem();
    }

    [Given("que tengo una contraseña temporal y todavía no tengo perfil médico")]
    public Task PrepararMedicoSinPerfil()
    {
        // El middleware JWT comprueba la vigencia contra el reloj real.
        scenario.Factory.Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return inicio.DadoUnMedicoConContrasenaTemporal(Email, Temporal);
    }

    [When("inicio sesión desde web con mi contraseña temporal")]
    public Task IniciarDesdeWeb() => IniciarSesionAsync(ClientTypeHeader.Web);

    [When("inicio sesión desde móvil con mi contraseña temporal")]
    public Task IniciarDesdeMovil() => IniciarSesionAsync(ClientTypeHeader.Mobile);

    [Then("recibo la indicación de continuar desde web")]
    public async Task VerificarAvisoMovil()
    {
        using var json = JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("requiresWebFirstAccess").GetBoolean().ShouldBeTrue();
        _sesionMovilId = LeerSesionId(InicioDeSesionSteps.ReadAccessToken(_login));
    }

    [When("inicio sesión desde web y cambio mi contraseña temporal")]
    public async Task ContinuarDesdeWeb()
    {
        await IniciarDesdeWeb();
        await inicio.ElPasoPendienteEs(PendingStepClaimValues.ChangeTemporaryPassword);
        await CambiarContrasena(8);
    }

    [When("intento asociar la especialidad")]
    [When("intento asociar la especialidad con el JWT anterior")]
    public Task AsociarConJwtOriginal() => AsociarAsync(_login.AccessToken!);

    [When("vuelvo a solicitar la misma asociación con el JWT nuevo")]
    public Task AsociarConJwtNuevo() => AsociarAsync(_cambio.AccessToken!);

    [Then("la API responde HTTP 403 y la asociación sigue sin existir")]
    public async Task VerificarBloqueo()
    {
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await TestDataSeeder.ReadDoctorsAsync(context, [_doctorId])).ShouldBe(_perfilAntesDeAsociar);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, [_doctorId])).ShouldBe(_asociacionesAntesDeAsociar);
    }

    [When("establezco una contraseña distinta de exactamente {int} caracteres")]
    public async Task CambiarContrasena(int longitud)
    {
        Nueva.Length.ShouldBe(longitud);
        string.Equals(Nueva, Temporal, StringComparison.Ordinal).ShouldBeFalse();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-temporary-password")
        {
            Content = JsonContent.Create(new ChangeTemporaryPasswordRequest(Nueva))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _login.AccessToken);
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.SendAsync(request);
    }

    [Then("recibo credenciales nuevas sin paso pendiente")]
    public async Task VerificarCambioConPerfil()
    {
        await VerificarCambioAsync(sesionesEsperadas: 1);
        await inicio.NoHayPasoPendiente();
    }

    [Then("las credenciales nuevas indican el paso pendiente {string}")]
    public async Task VerificarCambioSinPerfil(string paso)
    {
        var user = await VerificarCambioAsync(sesionesEsperadas: 2);
        await inicio.ElPasoPendienteEs(paso);
        var sesionMovil = user.Sessions.Single(session => session.Id == _sesionMovilId);
        sesionMovil.RevokedAt.ShouldNotBeNull();
        sesionMovil.RevocationReason.ShouldBe(SessionRevocationReason.PasswordReset);
    }

    [Then("la API responde HTTP 204 y la asociación queda guardada")]
    public async Task VerificarAsociacion()
    {
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await context.Doctors.AsNoTracking().SingleAsync(value => value.Id == _doctorId);
        var user = (await scenario.Factory.FindUserAsync(Email)).ShouldNotBeNull();
        doctor.UserId.ShouldBe(user.Id);
        doctor.Specialties.Select(link => link.SpecialtyId)
            .ShouldBe(new[] { _especialidadInicialId, _especialidadObjetivoId }, ignoreOrder: true);
    }

    [Then("la contraseña temporal ya no permite iniciar sesión")]
    public async Task VerificarContrasenaTemporalRechazada()
    {
        await inicio.LoginAsync(Email, Temporal, ClientTypeHeader.Web);
        await inicio.ElInicioDeSesionEsRechazado(UserErrors.InvalidCredentials.Code);
    }

    private async Task IniciarSesionAsync(string cliente)
    {
        var antes = (await scenario.Factory.FindUserAsync(Email)).ShouldNotBeNull();
        await inicio.LoginAsync(Email, Temporal, cliente);
        _login = await inicio.ReadSuccessfulLoginAsync();
        var user = (await scenario.Factory.FindUserAsync(Email)).ShouldNotBeNull();
        var sessionId = await VerificarCredencialesAsync(_login, user);
        user.MustChangePassword.ShouldBeTrue();
        user.Sessions.Select(session => session.Id)
            .ShouldBe(antes.Sessions.Select(session => session.Id).Append(sessionId), ignoreOrder: true);

        if (cliente == ClientTypeHeader.Web)
        {
            using var json = JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());
            json.RootElement.TryGetProperty("requiresWebFirstAccess", out _).ShouldBeFalse();
        }
    }

    private async Task<User> VerificarCambioAsync(int sesionesEsperadas)
    {
        _cambio = await inicio.ReadSuccessfulLoginAsync();
        var user = (await scenario.Factory.FindUserAsync(Email)).ShouldNotBeNull();
        var sessionId = await VerificarCredencialesAsync(_cambio, user);
        sessionId.ShouldBe(LeerSesionId(InicioDeSesionSteps.ReadAccessToken(_login)));
        user.Sessions.Count.ShouldBe(sesionesEsperadas);
        user.MustChangePassword.ShouldBeFalse();
        var hasher = scenario.Factory.Services.GetRequiredService<IPasswordHasher>();
        hasher.Verify(user.PasswordHash, Nueva).ShouldBeTrue();
        hasher.Verify(user.PasswordHash, Temporal).ShouldBeFalse();
        string.Equals(_cambio.AccessToken, _login.AccessToken, StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(_cambio.RefreshToken, _login.RefreshToken, StringComparison.Ordinal).ShouldBeFalse();
        return user;
    }

    private async Task<int> VerificarCredencialesAsync(LoginResponse respuesta, User user)
    {
        respuesta.RequiresTwoFactor.ShouldBeFalse();
        respuesta.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        var jwt = InicioDeSesionSteps.ReadAccessToken(respuesta);
        var bearer = scenario.Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var validacion = await new JsonWebTokenHandler().ValidateTokenAsync(respuesta.AccessToken!, bearer.TokenValidationParameters);
        validacion.IsValid.ShouldBeTrue();
        jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(user.Id.ToString(CultureInfo.InvariantCulture));
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
        var sessionId = LeerSesionId(jwt);
        var session = user.Sessions.Single(value => value.Id == sessionId);
        session.IsActiveAt(scenario.Factory.Clock.UtcNow).ShouldBeTrue();
        var tokenGenerator = scenario.Factory.Services.GetRequiredService<ISecureTokenGenerator>();
        string.Equals(session.RefreshTokenHash, tokenGenerator.Hash(respuesta.RefreshToken!), StringComparison.Ordinal)
            .ShouldBeTrue("El refresh token debe corresponder a la sesión persistida.");
        return sessionId;
    }

    private async Task AsociarAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/doctors/me/specialties/{_especialidadObjetivoId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.SendAsync(request);
    }

    private static int LeerSesionId(JsonWebToken jwt) =>
        int.Parse(jwt.GetClaim(AppClaims.SessionId).Value, CultureInfo.InvariantCulture);

    private sealed record Ubicacion(int ProvinciaId, int MunicipioId);
}
