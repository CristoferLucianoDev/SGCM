using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;
using static SGCM.Test.Common.Database.TestDataSeeder;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class AsociacionYDesasociacionDeEspecialidadesDeDoctoresSteps(ApiScenario scenario)
{
    private const string Password = "Secreta123";
    private DoctorSpecialtyData _data = null!;
    private DoctorSpecialtySnapshot _before = null!;
    private User _a = null!;
    private User _b = null!;
    private int _target;
    private int _appointmentId;
    private int _patientId;
    private int _patientUserId;
    private string? _appointmentStatus;

    [Given("que preparo dos médicos independientes para gestionar sus especialidades")]
    public async Task PrepararMedicos()
    {
        scenario.Factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        _a = await CreateUserAsync(UserRole.Doctor);
        _b = await CreateUserAsync(UserRole.Doctor);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        _data = await InsertDoctorSpecialtyDataAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _a.Id, _b.Id);
        _data.DoctorAId.ShouldNotBe(_a.Id);
        _data.DoctorBId.ShouldNotBe(_b.Id);
        _data.DoctorAId.ShouldNotBe(_data.DoctorBId);
        _a.Id.ShouldNotBe(_b.Id);
        new[] { _a.Id, _b.Id, _data.DoctorAId, _data.DoctorBId }.Distinct().Count().ShouldBe(4);
    }

    [Given("que el perfil de Doctor A conserva {string} especialidades")]
    public async Task PrepararCantidad(string count)
    {
        if (count == "dos") return;
        count.ShouldBe("una");
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM people.doctor_specialty WHERE doctor_id = {_data.DoctorAId} AND specialty_id = {_data.SpecialtyIds[1]}"))
            .ShouldBe(1);
    }

    [Given("que la especialidad objetivo del médico es {string}")]
    public async Task PrepararObjetivo(string kind)
    {
        _target = kind switch
        {
            "nueva" => _data.SpecialtyIds[2],
            "asociada" or "asociada inactiva" => _data.SpecialtyIds[0],
            "inactiva" => _data.SpecialtyIds[3],
            "inexistente" => _data.SpecialtyIds.Max() + 1,
            "cero" => 0,
            "negativo" => -1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (kind == "asociada inactiva")
        {
            await using var scope = scenario.Factory.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.specialty SET is_active = false WHERE id = {_target}")).ShouldBe(1);
        }
    }

    [Given("que inicio sesión para gestionar asociaciones como {string}")]
    public async Task IniciarSesion(string roleName)
    {
        var role = Enum.Parse<UserRole>(roleName);
        var user = role == UserRole.Doctor ? _a : await CreateUserAsync(role);
        if (role != UserRole.Doctor)
        {
            await using var scope = scenario.Factory.Services.CreateAsyncScope();
            await InsertProfileAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), user);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email.Value, password = Password })
        };
        request.Headers.UserAgent.ParseAdd("PruebasSGCM/1.0");
        using var response = await scenario.Client.SendAsync(request);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        login.PendingStep.ShouldBeNull();
        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        var jwt = new JsonWebToken(login.AccessToken);
        jwt.Claims.Where(claim => claim.Type == AppClaims.UserId).Select(claim => claim.Value)
            .ShouldBe(new[] { user.Id.ToString(CultureInfo.InvariantCulture) });
        jwt.Claims.Where(claim => claim.Type == AppClaims.Role).Select(claim => claim.Value)
            .ShouldBe(new[] { roleName });
        jwt.Claims.ShouldNotContain(claim => claim.Type == AppClaims.PendingStep);
        scenario.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    [Given("que existe una cita médica {string} para esa especialidad")]
    public async Task PrepararCita(string status)
    {
        var patient = await CreateUserAsync(UserRole.Patient);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _appointmentId = await InsertAppointmentAsync(context,
            patient.Id, _data.DoctorAId, _target, status);
        _patientId = await context.Patients.Where(value => value.UserId == patient.Id)
            .Select(value => value.Id).SingleAsync();
        _patientUserId = patient.Id;
        _appointmentStatus = status;
    }

    [When("solicito {string} la especialidad del perfil médico")]
    public Task SolicitarOperacion(string operation) => SendAsync(operation, false);

    [When("solicito {string} enviando los identificadores del otro médico")]
    public Task IntentarCambiarPropietario(string operation) => SendAsync(operation, true);

    private async Task SendAsync(string operation, bool foreignOwner)
    {
        _before = await SnapshotAsync(); // Después de login y antes de la operación real.
        if (_appointmentId > 0) VerifyAppointmentBeforeRequest();
        var url = $"/api/doctors/me/specialties/{_target}";
        if (foreignOwner) url += $"?doctorId={_data.DoctorBId}&userId={_b.Id}";
        using var request = new HttpRequestMessage(operation switch
        {
            "asociar" => HttpMethod.Post,
            "desasociar" => HttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        }, url);
        if (foreignOwner)
        {
            request.Content = JsonContent.Create(new { doctorId = _data.DoctorBId, userId = _b.Id });
            request.Headers.Add("X-Doctor-Id", _data.DoctorBId.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-User-Id", _b.Id.ToString(CultureInfo.InvariantCulture));
        }
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.SendAsync(request);
    }

    [Then("el ProblemDetails de asociaciones tiene código {string}")]
    public async Task VerificarError(string code)
    {
        var response = scenario.Response!;
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe((int)response.StatusCode);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Then("los perfiles médicos y todos sus datos persistidos permanecen intactos")]
    public async Task VerificarSinCambios()
    {
        var after = await SnapshotAsync();
        VerifyUnchangedData(after);
        after.Links.ShouldBe(_before.Links);
    }

    [Then("PostgreSQL refleja únicamente la operación médica {string}")]
    public async Task VerificarOperacion(string operation)
    {
        var response = scenario.Response!;
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        var after = await SnapshotAsync();
        VerifyUnchangedData(after);
        bool Target(string row) => Field(row, "doctor_id") == _data.DoctorAId && Field(row, "specialty_id") == _target;
        if (operation == "asociar")
        {
            _before.Links.Where(Target).ShouldBeEmpty();
            after.Links.Where(Target).ShouldHaveSingleItem();
            after.Links.Where(row => !Target(row)).ShouldBe(_before.Links);
        }
        else
        {
            operation.ShouldBe("desasociar");
            _before.Links.Where(Target).ShouldHaveSingleItem();
            after.Links.ShouldBe(_before.Links.Where(row => !Target(row)));
        }
        after.Links.Where(row => Field(row, "doctor_id") == _data.DoctorAId).ShouldNotBeEmpty();
        after.Links.Where(row => Field(row, "doctor_id") == _data.DoctorBId)
            .ShouldBe(_before.Links.Where(row => Field(row, "doctor_id") == _data.DoctorBId));
    }

    [Then("la cita médica conserva exactamente todos sus campos persistidos")]
    public async Task VerificarCita()
    {
        _appointmentId.ShouldBeGreaterThan(0);
        var after = await SnapshotAsync();
        after.Appointments.ShouldBe(_before.Appointments);
        after.Availability.ShouldBe(_before.Availability);
    }

    private void VerifyAppointmentBeforeRequest()
    {
        _appointmentStatus.ShouldNotBeNull();
        _before.Links.Where(value => Field(value, "doctor_id") == _data.DoctorAId)
            .Select(value => Field(value, "specialty_id")).ShouldBe(_data.SpecialtyIds.Take(2));
        _before.Appointments.ShouldHaveSingleItem();
        using var document = JsonDocument.Parse(_before.Appointments[0]);
        var row = document.RootElement;
        row.GetProperty("id").GetInt32().ShouldBe(_appointmentId);
        row.GetProperty("patient_id").GetInt32().ShouldBe(_patientId);
        row.GetProperty("doctor_id").GetInt32().ShouldBe(_data.DoctorAId);
        row.GetProperty("specialty_id").GetInt32().ShouldBe(_target);
        row.GetProperty("status").GetString().ShouldBe(_appointmentStatus);
        row.GetProperty("reason_for_visit").GetString().ShouldBe("Consulta previamente agendada");
        row.GetProperty("status_reason").GetString().ShouldBe("Reserva de prueba");
        row.GetProperty("created_by").GetInt32().ShouldBe(_patientUserId);
        row.GetProperty("ends_at").GetDateTimeOffset().ShouldBeGreaterThan(row.GetProperty("starts_at").GetDateTimeOffset());
        _before.Availability.ShouldHaveSingleItem();
        using var block = JsonDocument.Parse(_before.Availability[0]);
        block.RootElement.GetProperty("doctor_id").GetInt32().ShouldBe(_data.DoctorAId);
        block.RootElement.GetProperty("series_id").ValueKind.ShouldBe(JsonValueKind.Null);
        block.RootElement.GetProperty("is_available").GetBoolean().ShouldBeTrue();
        block.RootElement.GetProperty("starts_at").GetDateTimeOffset()
            .ShouldBeLessThanOrEqualTo(row.GetProperty("starts_at").GetDateTimeOffset());
        block.RootElement.GetProperty("ends_at").GetDateTimeOffset()
            .ShouldBeGreaterThanOrEqualTo(row.GetProperty("ends_at").GetDateTimeOffset());
    }

    private void VerifyUnchangedData(DoctorSpecialtySnapshot after)
    {
        after.Doctors.ShouldBe(_before.Doctors);
        after.Specialties.ShouldBe(_before.Specialties);
        after.Appointments.ShouldBe(_before.Appointments);
        after.Availability.ShouldBe(_before.Availability);
    }

    private async Task<User> CreateUserAsync(UserRole role)
    {
        var user = UserMother.WithRole(role, email: Email.Create($"bdd-doctor-specialty-{Guid.NewGuid():N}@correo.com").Value,
            passwordHash: scenario.Factory.HashPassword(Password));
        await scenario.Factory.SaveAsync(user);
        return user;
    }

    private async Task<DoctorSpecialtySnapshot> SnapshotAsync()
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await ReadDoctorSpecialtySnapshotAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _data.DoctorIds);
    }

    private static int Field(string row, string name)
    {
        using var document = JsonDocument.Parse(row);
        return document.RootElement.GetProperty(name).GetInt32();
    }
}
