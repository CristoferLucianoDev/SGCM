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
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ActualizarPerfilDelMedicoSteps(ApiScenario scenario, InicioDeSesionSteps inicio)
{
    private const string Url = "/api/doctors/me/profile";
    private const string Password = "Sintetica256";
    private readonly string _email = $"medico256-{Guid.NewGuid():N}@correo.com";
    private int _userId;
    private int _doctorId;
    private int _sessionId;
    private string _accessToken = null!;
    private Location _originalLocation = null!;
    private Location _newLocation = null!;
    private int _incompatibleProvinceId;
    private UpdateDoctorProfileRequest _newProfile = null!;
    private DoctorIdentity _identity = null!;
    private Snapshot _before = null!;
    private int[] _activeSpecialtyIds = [];
    private string[] _activeSpecialtyNames = [];

    [Given("que tengo un perfil médico completo con especialidades asociadas")]
    public async Task PrepararPerfilMedicoAsync()
    {
        scenario.Factory.Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await inicio.DadoUnMedico(_email, Password);
        var user = (await scenario.Factory.FindUserAsync(_email)).ShouldNotBeNull();
        user.IsActive.ShouldBeTrue();
        user.IsEmailConfirmed.ShouldBeTrue();
        user.MustChangePassword.ShouldBeFalse();
        _userId = user.Id;

        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locations = await db.Database.SqlQuery<Location>($"""
            SELECT DISTINCT ON (p.id) p.id AS province_id, p.name AS province_name,
                m.id AS municipality_id, m.name AS municipality_name
            FROM people.province p
            JOIN people.municipality m ON m.province_id = p.id
            ORDER BY p.id, m.id LIMIT 3
            """).ToListAsync();
        locations.Count.ShouldBe(3);
        _originalLocation = locations[0];
        _newLocation = locations[1];
        _incompatibleProvinceId = locations[2].ProvinceId;
        _newLocation.ProvinceId.ShouldNotBe(_originalLocation.ProvinceId);
        _newLocation.MunicipalityId.ShouldNotBe(_originalLocation.MunicipalityId);
        _incompatibleProvinceId.ShouldNotBe(_originalLocation.ProvinceId);
        _incompatibleProvinceId.ShouldNotBe(_newLocation.ProvinceId);

        // Guarda primero la especialidad que va al final al ordenar por nombre.
        var last = Specialty.Create("Traumatología 256", "Especialidad activa.").Value;
        db.Specialties.Add(last);
        await db.SaveChangesAsync();
        var first = Specialty.Create("Cardiología 256", "Especialidad activa.").Value;
        var inactive = Specialty.Create("Medicina interna 256", "Especialidad inactiva.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        db.Specialties.AddRange(first, inactive);
        await db.SaveChangesAsync();
        _activeSpecialtyIds = [first.Id, last.Id];
        _activeSpecialtyNames = [first.Name, last.Name];
        var doctor = DoctorMother.Create([last.Id, first.Id, inactive.Id], userId: _userId, licenseNumber: $"LIC-256-{_userId}",
            provinceId: _originalLocation.ProvinceId, municipalityId: _originalLocation.MunicipalityId).Value;
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        _doctorId = doctor.Id;
        _identity = ReadIdentity(doctor);
        _newProfile = new("809-555-0256", _newLocation.ProvinceId, _newLocation.MunicipalityId,
            "Avenida de la Salud 256", 1800.75m, 14, "Medicina preventiva y seguimiento clínico.");

        var columns = await db.Database.SqlQuery<string>($"""
            SELECT column_name::text AS "Value" FROM information_schema.columns
            WHERE table_schema = 'people' AND table_name = 'vw_doctor_directory'
            """).ToListAsync();
        columns.ShouldBe(new[] { "id", "first_name", "last_name", "province_id", "province_name", "municipality_id",
            "municipality_name", "address_line", "consultation_fee", "years_of_experience", "biography",
            "specialty_ids", "specialty_names" }, ignoreOrder: true);
    }

    [Given("que inicié sesión con mi cuenta médica")]
    public async Task IniciarSesionMedicaAsync()
    {
        await inicio.LoginAsync(_email, Password);
        var login = await inicio.ReadSuccessfulLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        (login.PendingStep is null).ShouldBeTrue();
        string.IsNullOrWhiteSpace(login.AccessToken).ShouldBeFalse("El login debe incluir un JWT.");
        _accessToken = login.AccessToken!;

        var options = scenario.Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(_accessToken, options.TokenValidationParameters);
        validation.IsValid.ShouldBeTrue("El JWT debe ser válido para la API.");
        var jwt = (JsonWebToken)validation.SecurityToken;
        jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(_userId.ToString(CultureInfo.InvariantCulture));
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
        jwt.TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
        _sessionId = int.Parse(jwt.GetClaim(AppClaims.SessionId).Value, CultureInfo.InvariantCulture);
        await AssertSessionActiveAsync();

        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await ReadDoctorAsync(db);
        var directory = await ReadDirectoryAsync(db);
        AssertDirectory(directory, ExpectedDirectory(directory, ReadProfile(doctor), _originalLocation) with
        {
            SpecialtyIds = _activeSpecialtyIds, SpecialtyNames = _activeSpecialtyNames
        });
        directory.Id.ShouldBe(_doctorId);
        directory.FirstName.ShouldBe(_identity.FirstName);
        directory.LastName.ShouldBe(_identity.LastName);
        _before = await ReadSnapshotAsync(db, directory);
        _before.Doctor.ShouldHaveSingleItem();
        _before.Links.Length.ShouldBe(3);

        var oldProfile = ReadProfile(doctor);
        oldProfile.PhoneNumber.ShouldNotBe(_newProfile.PhoneNumber);
        oldProfile.ProvinceId.ShouldNotBe(_newProfile.ProvinceId);
        oldProfile.MunicipalityId.ShouldNotBe(_newProfile.MunicipalityId);
        oldProfile.AddressLine.ShouldNotBe(_newProfile.AddressLine);
        oldProfile.ConsultationFee.ShouldNotBe(_newProfile.ConsultationFee);
        oldProfile.YearsOfExperience.ShouldNotBe(_newProfile.YearsOfExperience);
        oldProfile.Biography.ShouldNotBe(_newProfile.Biography);
    }

    [When("guardo mis nuevos datos profesionales como médico")]
    public Task ActualizarDatosMedicosAsync() => PutAsync(_newProfile);

    [When("intento guardar mis datos profesionales con un municipio de otra provincia")]
    public Task ActualizarConUbicacionIncompatibleAsync() =>
        PutAsync(_newProfile with { ProvinceId = _incompatibleProvinceId });

    [Then("se guardan mis datos y el directorio refleja de inmediato los cambios")]
    public async Task VerificarActualizacionAsync()
    {
        var response = scenario.Response!;
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var directory = await ReadDirectoryAsync(db);
        AssertDirectory(directory, ExpectedDirectory(_before.Directory, _newProfile, _newLocation));

        var doctor = await ReadDoctorAsync(db);
        ReadProfile(doctor).ShouldBe(_newProfile);
        ReadIdentity(doctor).ShouldBe(_identity);
        var after = await ReadSnapshotAsync(db, directory);
        AssertLinksAndSessions(after);
        await AssertSessionActiveAsync();
    }

    [Then("se rechaza mi ubicación y el perfil y el directorio conservan sus datos")]
    public async Task VerificarRechazoAsync()
    {
        var response = scenario.Response!;
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string.Equals(await response.ReadErrorCodeAsync(), "Doctor.MunicipalityProvinceMismatch", StringComparison.Ordinal)
            .ShouldBeTrue("Se esperaba el error de municipio de otra provincia.");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.TryGetProperty("accessToken", out _).ShouldBeFalse();
        problem.RootElement.TryGetProperty("refreshToken", out _).ShouldBeFalse();

        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var directory = await ReadDirectoryAsync(db);
        AssertDirectory(directory, _before.Directory);
        var after = await ReadSnapshotAsync(db, directory);
        after.Doctor.ShouldBe(_before.Doctor);
        AssertLinksAndSessions(after);
        await AssertSessionActiveAsync();
    }

    private async Task PutAsync(UpdateDoctorProfileRequest profile)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Url) { Content = JsonContent.Create(profile) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.SendAsync(request);
    }

    private Task<Doctor> ReadDoctorAsync(AppDbContext db) =>
        db.Doctors.AsNoTracking().SingleAsync(doctor => doctor.Id == _doctorId && doctor.UserId == _userId);

    private Task<DirectoryRow> ReadDirectoryAsync(AppDbContext db) => db.Database.SqlQuery<DirectoryRow>($"""
        SELECT id, first_name, last_name, province_id, province_name, municipality_id, municipality_name,
            address_line, consultation_fee, years_of_experience, biography, specialty_ids, specialty_names
        FROM people.vw_doctor_directory WHERE id = {_doctorId}
        """).SingleAsync();

    private async Task<Snapshot> ReadSnapshotAsync(AppDbContext db, DirectoryRow directory) => new(
        await TestDataSeeder.ReadDoctorsAsync(db, [_doctorId]),
        await TestDataSeeder.ReadDoctorSpecialtiesAsync(db, [_doctorId]),
        await db.Database.SqlQuery<string>($"""
            SELECT COALESCE(jsonb_agg(to_jsonb(s) ORDER BY s.id)::text, '[]') AS "Value"
            FROM app.user_session s WHERE s.user_id = {_userId}
            """).SingleAsync(),
        directory);

    private async Task AssertSessionActiveAsync()
    {
        var user = (await scenario.Factory.FindUserAsync(_email)).ShouldNotBeNull();
        var session = user.Sessions.ShouldHaveSingleItem();
        session.Id.ShouldBe(_sessionId);
        session.IsActiveAt(scenario.Factory.Clock.UtcNow).ShouldBeTrue();
    }

    private void AssertLinksAndSessions(Snapshot after)
    {
        after.Links.ShouldBe(_before.Links);
        string.Equals(after.Sessions, _before.Sessions, StringComparison.Ordinal)
            .ShouldBeTrue("Las sesiones persistidas deben conservarse.");
    }

    private static DoctorIdentity ReadIdentity(Doctor doctor) =>
        new(doctor.Id, doctor.UserId, doctor.FirstName, doctor.LastName, doctor.LicenseNumber, doctor.IsActive);

    private static UpdateDoctorProfileRequest ReadProfile(Doctor doctor) =>
        new(doctor.PhoneNumber.Value, doctor.ProvinceId, doctor.MunicipalityId, doctor.AddressLine,
            doctor.ConsultationFee.Value, doctor.YearsOfExperience, doctor.Biography);

    private static DirectoryRow ExpectedDirectory(DirectoryRow original, UpdateDoctorProfileRequest profile, Location location) =>
        original with
        {
            ProvinceId = profile.ProvinceId,
            ProvinceName = location.ProvinceName,
            MunicipalityId = profile.MunicipalityId,
            MunicipalityName = location.MunicipalityName,
            AddressLine = profile.AddressLine!,
            ConsultationFee = profile.ConsultationFee,
            YearsOfExperience = profile.YearsOfExperience,
            Biography = profile.Biography!
        };

    private static void AssertDirectory(DirectoryRow actual, DirectoryRow expected)
    {
        actual.ShouldBe(expected with { SpecialtyIds = actual.SpecialtyIds, SpecialtyNames = actual.SpecialtyNames });
        actual.SpecialtyIds.ShouldBe(expected.SpecialtyIds);
        actual.SpecialtyNames.ShouldBe(expected.SpecialtyNames);
    }

    private sealed record Location(int ProvinceId, string ProvinceName, int MunicipalityId, string MunicipalityName);
    private sealed record DoctorIdentity(int Id, int UserId, string FirstName, string LastName, string License, bool Active);
    private sealed record DirectoryRow(int Id, string FirstName, string LastName, int ProvinceId, string ProvinceName,
        int MunicipalityId, string MunicipalityName, string AddressLine, decimal ConsultationFee, int YearsOfExperience,
        string Biography, int[] SpecialtyIds, string[] SpecialtyNames);
    private sealed record Snapshot(string[] Doctor, string[] Links, string Sessions, DirectoryRow Directory);
}
