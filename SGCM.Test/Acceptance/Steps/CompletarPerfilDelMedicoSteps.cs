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
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Common;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class CompletarPerfilDelMedicoSteps(ApiScenario scenario, InicioDeSesionSteps inicio)
{
    private const string PerfilUrl = "/api/doctors/profile";
    private const string ContrasenaDefinitiva = "Definitiva253";

    private readonly string _correo = $"medico253-{Guid.NewGuid():N}@correo.com";
    private string _contrasenaTemporal = null!;
    private int _usuarioId;
    private int _otraProvinciaId;
    private int _segundaEspecialidadId;
    private CompleteDoctorProfileRequest _perfil = null!;
    private SesionAutenticada _inicial = null!;
    private SesionAutenticada _pendiente = null!;
    private SesionAutenticada _alta = null!;
    private SesionAutenticada _siguiente = null!;
    private int[] _sesionesAntesDelSiguienteLogin = [];
    private int _doctorId;
    private string[] _filaDelPerfil = [];
    private EstadoPersistido _antesDelRechazo = null!;
    private int _titularId;
    private string[] _filaTitular = [];
    private string[] _vinculosTitular = [];

    [Given("que tengo una cuenta médica nueva y datos profesionales válidos")]
    public async Task PrepararCuentaYPerfilAsync()
    {
        // JwtBearer comprueba la vigencia contra el reloj real.
        scenario.Factory.Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateDoctorAccountCommand, CreateDoctorAccountResult>>();
        var creada = await handler.HandleAsync(new CreateDoctorAccountCommand(_correo), CancellationToken.None);
        creada.IsSuccess.ShouldBeTrue("Debe poder aprovisionarse la cuenta médica de prueba.");
        _usuarioId = creada.Value.UserId;
        _contrasenaTemporal = creada.Value.TemporaryPassword;
        string.Equals(_contrasenaTemporal, ContrasenaDefinitiva, StringComparison.Ordinal).ShouldBeFalse();

        var usuario = await LeerUsuarioAsync();
        usuario.Role.ShouldBe(UserRole.Doctor);
        usuario.MustChangePassword.ShouldBeTrue();
        usuario.Sessions.Count.ShouldBe(0);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Doctors.AnyAsync(doctor => doctor.UserId == _usuarioId)).ShouldBeFalse();
        var ubicacion = await db.Database.SqlQuery<Ubicacion>($"""
            SELECT id AS municipio_id, province_id AS provincia_id
            FROM people.municipality ORDER BY id LIMIT 1
            """).SingleAsync();
        _otraProvinciaId = await db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM people.province
            WHERE id <> {ubicacion.ProvinciaId} ORDER BY id LIMIT 1
            """).SingleAsync();

        var primera = Specialty.Create("Medicina interna 253", "Especialidad inicial de prueba.").Value;
        var segunda = Specialty.Create("Endocrinología 253", "Especialidad para asociar después del login.").Value;
        db.Specialties.AddRange(primera, segunda);
        await db.SaveChangesAsync();
        _segundaEspecialidadId = segunda.Id;
        _perfil = new("Ana", "Gómez", $"LIC-253-{Guid.NewGuid():N}", "809-555-0123",
            ubicacion.ProvinciaId, ubicacion.MunicipioId, "Avenida de Pruebas 253", 1250.75m,
            8, "Atención médica de adultos.", [primera.Id]);
    }

    [When("inicio mi primer acceso médico desde web")]
    public Task IniciarPorPrimeraVezAsync() => inicio.LoginAsync(_correo, _contrasenaTemporal, ClientTypeHeader.Web);

    [Then("debo cambiar mi contraseña temporal")]
    public async Task VerificarLoginInicialAsync() =>
        _inicial = await LeerCredencialesAsync(PendingStepClaimValues.ChangeTemporaryPassword);

    [When("establezco mi contraseña definitiva para el primer acceso")]
    public Task CambiarContrasenaAsync() => EnviarAsync("/api/auth/change-temporary-password",
        new ChangeTemporaryPasswordRequest(ContrasenaDefinitiva), _inicial.AccessToken);

    [Then("debo completar mi perfil médico")]
    public async Task VerificarCambioAsync()
    {
        _pendiente = await LeerCredencialesAsync(PendingStepClaimValues.DoctorProfile);
        _pendiente.Id.ShouldBe(_inicial.Id);
        (await LeerUsuarioAsync()).MustChangePassword.ShouldBeFalse();
    }

    [When("envío los datos de mi perfil médico")]
    public Task EnviarPerfilAsync() => EnviarAsync(PerfilUrl, _perfil, _pendiente.AccessToken);

    [Then("mi perfil médico queda registrado y puedo continuar")]
    public async Task VerificarAltaAsync()
    {
        _alta = await LeerCredencialesAsync(paso: null);
        _alta.Id.ShouldNotBe(_pendiente.Id);
        using var json = await LeerJsonAsync();
        json.RootElement.GetProperty("pendingStep").ValueKind.ShouldBe(JsonValueKind.Null);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await db.Doctors.AsNoTracking().SingleAsync(doctor => doctor.UserId == _usuarioId);
        _doctorId = doctor.Id;
        doctor.FirstName.ShouldBe(_perfil.FirstName);
        doctor.LastName.ShouldBe(_perfil.LastName);
        doctor.LicenseNumber.ShouldBe(_perfil.LicenseNumber);
        doctor.PhoneNumber.Value.ShouldBe(_perfil.PhoneNumber);
        doctor.ProvinceId.ShouldBe(_perfil.ProvinceId);
        doctor.MunicipalityId.ShouldBe(_perfil.MunicipalityId);
        doctor.AddressLine.ShouldBe(_perfil.AddressLine);
        doctor.ConsultationFee.Value.ShouldBe(_perfil.ConsultationFee);
        doctor.YearsOfExperience.ShouldBe(_perfil.YearsOfExperience);
        doctor.Biography.ShouldBe(_perfil.Biography);
        doctor.IsActive.ShouldBeTrue();
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(_perfil.SpecialtyIds!);
        _filaDelPerfil = await TestDataSeeder.ReadDoctorsAsync(db, [_doctorId]);
        _filaDelPerfil.ShouldHaveSingleItem();
    }

    [When("cierro la sesión que recibí al completar el perfil médico")]
    public async Task CerrarSesionDelAltaAsync()
    {
        await EnviarAsync("/api/auth/logout", new LogoutRequest(_alta.RefreshToken));
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var usuario = await LeerUsuarioAsync();
        var cerrada = usuario.Sessions.Single(sesion => sesion.Id == _alta.Id);
        cerrada.RevokedAt.ShouldNotBeNull();
        cerrada.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        usuario.Sessions.Single(sesion => sesion.Id == _pendiente.Id).IsActiveAt(scenario.Factory.Clock.UtcNow)
            .ShouldBeTrue("El cierre corresponde a la sesión del alta.");
    }

    [When("vuelvo a iniciar sesión como médico desde {string}")]
    public async Task IniciarDeNuevoAsync(string cliente)
    {
        _sesionesAntesDelSiguienteLogin = (await LeerUsuarioAsync()).Sessions.Select(sesion => sesion.Id).ToArray();
        await inicio.LoginAsync(_correo, ContrasenaDefinitiva, clientType: cliente);
        var solicitud = scenario.Response!.RequestMessage.ShouldNotBeNull();
        (solicitud.Headers.Authorization is null)
            .ShouldBeTrue("El nuevo login debe usar únicamente las credenciales de la cuenta.");
        (solicitud.Headers.TryGetValues(ClientTypeHeader.Name, out var clientes)
            && clientes.SequenceEqual([cliente], StringComparer.Ordinal))
            .ShouldBeTrue("El nuevo login debe declarar el cliente indicado en el escenario.");
    }

    [Then("puedo continuar sin pasos pendientes")]
    public async Task VerificarSiguienteLoginAsync()
    {
        _siguiente = await LeerCredencialesAsync(paso: null);
        using var json = await LeerJsonAsync();
        json.RootElement.GetProperty("pendingStep").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.TryGetProperty("requiresWebFirstAccess", out _).ShouldBeFalse();
        var usuario = await LeerUsuarioAsync();
        usuario.MustChangePassword.ShouldBeFalse();
        usuario.Sessions.Select(sesion => sesion.Id)
            .ShouldBe(_sesionesAntesDelSiguienteLogin.Append(_siguiente.Id), ignoreOrder: true);
        _sesionesAntesDelSiguienteLogin.ShouldNotContain(_siguiente.Id);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await TestDataSeeder.ReadDoctorsAsync(db, [_doctorId])).ShouldBe(_filaDelPerfil);
    }

    [When("asocio una segunda especialidad con mi nueva sesión")]
    public Task AsociarSegundaEspecialidadAsync() =>
        EnviarAsync($"/api/doctors/me/specialties/{_segundaEspecialidadId}", bearer: _siguiente.AccessToken);

    [Then("ambas especialidades quedan asociadas a mi perfil médico")]
    public async Task VerificarAsociacionAsync()
    {
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var autorizacion = scenario.Response.RequestMessage?.Headers.Authorization;
        (autorizacion?.Scheme == "Bearer"
            && string.Equals(autorizacion.Parameter, _siguiente.AccessToken, StringComparison.Ordinal))
            .ShouldBeTrue("La asociación debe usar el JWT emitido por el nuevo login.");
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await db.Doctors.AsNoTracking().SingleAsync(doctor => doctor.UserId == _usuarioId);
        doctor.Specialties.Select(link => link.SpecialtyId)
            .ShouldBe(new[] { _perfil.SpecialtyIds!.Single(), _segundaEspecialidadId }, ignoreOrder: true);
        (await TestDataSeeder.ReadDoctorsAsync(db, [_doctorId])).ShouldBe(_filaDelPerfil);
    }

    [Given("que tengo pendiente completar mi perfil médico")]
    public async Task PrepararMedicoPendienteAsync()
    {
        await PrepararCuentaYPerfilAsync();
        await IniciarPorPrimeraVezAsync();
        await VerificarLoginInicialAsync();
        await CambiarContrasenaAsync();
        await VerificarCambioAsync();
    }

    [When("envío mi perfil médico con un municipio de otra provincia")]
    public async Task EnviarMunicipioDeOtraProvinciaAsync()
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        _antesDelRechazo = await LeerEstadoPersistidoAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        _antesDelRechazo.Perfiles.ShouldBe(0);
        _antesDelRechazo.Vinculos.ShouldBe(0);
        await EnviarAsync(PerfilUrl, _perfil with { ProvinceId = _otraProvinciaId }, _pendiente.AccessToken);
    }

    [Then("se rechaza el perfil médico por la ubicación seleccionada")]
    public Task VerificarRechazoDeUbicacionAsync() => VerificarRechazoAsync(HttpStatusCode.BadRequest,
        "Doctor.MunicipalityProvinceMismatch", "El municipio debe pertenecer a la provincia seleccionada.");

    [Given("otro médico ya tiene registrada la licencia que voy a enviar")]
    public async Task PrepararTitularDeLicenciaAsync()
    {
        var correoTitular = $"titular253-{Guid.NewGuid():N}@correo.com";
        await inicio.DadoUnMedico(correoTitular, ContrasenaDefinitiva);
        var titular = (await scenario.Factory.FindUserAsync(correoTitular)).ShouldNotBeNull();
        titular.Id.ShouldNotBe(_usuarioId);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = DoctorMother.Create([_segundaEspecialidadId], userId: titular.Id,
            licenseNumber: _perfil.LicenseNumber, provinceId: _perfil.ProvinceId,
            municipalityId: _perfil.MunicipalityId).Value;
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        _titularId = doctor.Id;
        _filaTitular = await TestDataSeeder.ReadDoctorsAsync(db, [_titularId]);
        _vinculosTitular = await TestDataSeeder.ReadDoctorSpecialtiesAsync(db, [_titularId]);
        _filaTitular.ShouldHaveSingleItem();
        _vinculosTitular.ShouldHaveSingleItem();
        _antesDelRechazo = await LeerEstadoPersistidoAsync(db);
        _antesDelRechazo.Perfiles.ShouldBe(1);
        _antesDelRechazo.Vinculos.ShouldBe(1);
    }

    [Then("se rechaza mi licencia duplicada y se conserva el perfil del titular")]
    public async Task VerificarConflictoDeLicenciaAsync()
    {
        await VerificarRechazoAsync(HttpStatusCode.Conflict,
            "Doctor.LicenseNumberAlreadyInUse", "El número de licencia profesional ya está registrado.");
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await TestDataSeeder.ReadDoctorsAsync(db, [_titularId])).ShouldBe(_filaTitular);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(db, [_titularId])).ShouldBe(_vinculosTitular);
    }

    [Then("puedo renovar mi sesión y aún debo completar mi perfil médico")]
    public async Task RenovarSesionPendienteAsync()
    {
        await EnviarAsync("/api/auth/refresh", new RefreshSessionRequest(_pendiente.RefreshToken));
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renovada = (await scenario.Response.Content.ReadFromJsonAsync<RefreshSessionResponse>()).ShouldNotBeNull();
        string.Equals(renovada.PendingStep, PendingStepClaimValues.DoctorProfile, StringComparison.Ordinal)
            .ShouldBeTrue("La renovación debe conservar el perfil médico pendiente.");
        var sesion = await VerificarTokensAsync(renovada.AccessToken, renovada.RefreshToken, PendingStepClaimValues.DoctorProfile);
        sesion.Id.ShouldBe(_pendiente.Id);
    }

    private async Task VerificarRechazoAsync(HttpStatusCode estado, string codigo, string titulo)
    {
        var respuesta = scenario.Response!;
        respuesta.StatusCode.ShouldBe(estado);
        string.Equals(await respuesta.ReadErrorCodeAsync(), codigo, StringComparison.Ordinal)
            .ShouldBeTrue($"Se esperaba el código {codigo}.");
        using var json = await LeerJsonAsync();
        json.RootElement.GetProperty("status").GetInt32().ShouldBe((int)estado);
        string.Equals(json.RootElement.GetProperty("title").GetString(), titulo, StringComparison.Ordinal)
            .ShouldBeTrue("El rechazo debe devolver el mensaje público esperado.");
        json.RootElement.EnumerateObject()
            .All(property => property.Name is "type" or "title" or "status" or "code")
            .ShouldBeTrue("El error debe contener solo ProblemDetails, sin credenciales ni detalles internos.");

        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Doctors.AnyAsync(doctor => doctor.UserId == _usuarioId)).ShouldBeFalse();
        var despues = await LeerEstadoPersistidoAsync(db);
        despues.Perfiles.ShouldBe(_antesDelRechazo.Perfiles);
        despues.Vinculos.ShouldBe(_antesDelRechazo.Vinculos);
        despues.Sesiones.ShouldBe(_antesDelRechazo.Sesiones);
    }

    private async Task<SesionAutenticada> LeerCredencialesAsync(string? paso)
    {
        var login = await inicio.ReadSuccessfulLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        string.Equals(login.PendingStep, paso, StringComparison.Ordinal)
            .ShouldBeTrue($"El cuerpo debe indicar el paso esperado: {paso ?? "ninguno"}.");
        return await VerificarTokensAsync(login.AccessToken, login.RefreshToken, paso);
    }

    private async Task<SesionAutenticada> VerificarTokensAsync(string? accessToken, string? refreshToken, string? paso)
    {
        string.IsNullOrWhiteSpace(accessToken).ShouldBeFalse("La respuesta debe contener un access token.");
        string.IsNullOrWhiteSpace(refreshToken).ShouldBeFalse("La respuesta debe contener un refresh token.");
        var opciones = scenario.Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var validacion = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken!, opciones.TokenValidationParameters);
        validacion.IsValid.ShouldBeTrue("El JWT debe ser válido con la configuración de la API.");
        var jwt = (JsonWebToken)validacion.SecurityToken;
        jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(_usuarioId.ToString(CultureInfo.InvariantCulture));
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
        var tienePaso = jwt.TryGetClaim(AppClaims.PendingStep, out var claim);
        tienePaso.ShouldBe(paso is not null);
        string.Equals(claim?.Value, paso, StringComparison.Ordinal).ShouldBeTrue("El paso del JWT debe coincidir con el esperado.");
        var sid = int.Parse(jwt.GetClaim(AppClaims.SessionId).Value, CultureInfo.InvariantCulture);

        var usuario = await LeerUsuarioAsync();
        var sesion = usuario.Sessions.Single(value => value.Id == sid);
        sesion.IsActiveAt(scenario.Factory.Clock.UtcNow).ShouldBeTrue();
        var generador = scenario.Factory.Services.GetRequiredService<ISecureTokenGenerator>();
        string.Equals(sesion.RefreshTokenHash, generador.Hash(refreshToken!), StringComparison.Ordinal)
            .ShouldBeTrue("El refresh debe corresponder a la sesión persistida.");
        return new(sid, accessToken!, refreshToken!);
    }

    private async Task<User> LeerUsuarioAsync() => (await scenario.Factory.FindUserAsync(_correo)).ShouldNotBeNull();

    private async Task EnviarAsync(string ruta, object? cuerpo = null, string? bearer = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        if (cuerpo is not null) solicitud.Content = JsonContent.Create(cuerpo);
        if (bearer is not null) solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.SendAsync(solicitud);
    }

    private async Task<JsonDocument> LeerJsonAsync() =>
        JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

    private static async Task<EstadoPersistido> LeerEstadoPersistidoAsync(AppDbContext db) => new(
        await db.Doctors.CountAsync(),
        await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM people.doctor_specialty").SingleAsync(),
        await db.Set<UserSession>().OrderBy(sesion => sesion.Id)
            .Select(sesion => new EstadoSesion(
                sesion.Id, sesion.ExpiresAt, sesion.LastUsedAt, sesion.RevokedAt, sesion.RevocationReason))
            .ToArrayAsync());

    private sealed record Ubicacion(int MunicipioId, int ProvinciaId);
    private sealed record EstadoPersistido(int Perfiles, int Vinculos, EstadoSesion[] Sesiones);
    private sealed record EstadoSesion(
        int Id, DateTimeOffset Vence, DateTimeOffset UltimoUso, DateTimeOffset? Revocada, SessionRevocationReason? Motivo);

    private sealed class SesionAutenticada(int id, string accessToken, string refreshToken)
    {
        public int Id { get; } = id;
        public string AccessToken { get; } = accessToken;
        public string RefreshToken { get; } = refreshToken;
    }
}
