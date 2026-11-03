using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;
using static SGCM.Test.Common.Database.TestDataSeeder;

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class DoctorSpecialtyTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Password = "Secreta123";
    private DoctorSpecialtyData _data = null!;
    private User _a = null!;
    private User _b = null!;

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Doctor_modifica_solo_su_vinculo_con_JWT_real_y_conserva_el_resto(string operation)
    {
        await PrepareAsync();
        await AuthenticateAsync(_a);
        var target = _data.SpecialtyIds[operation == "POST" ? 2 : 0];
        var before = await SnapshotAsync();
        using var response = await SendAsync(operation, target);
        await AssertSuccessAsync(response);
        await AssertStateAsync(before, operation, target);
    }

    [Theory]
    [InlineData("POST", "duplicada", 409, "Doctor.SpecialtyAlreadyAssociated")]
    [InlineData("POST", "inexistente", 404, "Specialty.NotFound")]
    [InlineData("POST", "inactiva", 409, "Specialty.Inactive")]
    [InlineData("POST", "cero", 400, "Specialty.InvalidId")]
    [InlineData("POST", "negativo", 400, "Specialty.InvalidId")]
    [InlineData("DELETE", "ajena", 409, "Doctor.SpecialtyNotAssociated")]
    [InlineData("DELETE", "ultima", 409, "Doctor.CannotRemoveLastSpecialty")]
    [InlineData("DELETE", "ajenaConUna", 409, "Doctor.SpecialtyNotAssociated")]
    [InlineData("DELETE", "cero", 400, "Specialty.InvalidId")]
    [InlineData("DELETE", "negativo", 400, "Specialty.InvalidId")]
    public async Task Errores_devuelven_ProblemDetails_y_no_modifican_filas(
        string operation, string kind, int status, string code)
    {
        await PrepareAsync();
        if (kind is "ultima" or "ajenaConUna")
            await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM people.doctor_specialty WHERE doctor_id = {_data.DoctorAId} AND specialty_id = {_data.SpecialtyIds[1]}"));
        await AuthenticateAsync(_a);
        var target = kind switch
        {
            "cero" => 0,
            "negativo" => -1,
            "inexistente" => _data.SpecialtyIds.Max() + 1,
            "inactiva" => _data.SpecialtyIds[3],
            "ajena" or "ajenaConUna" => _data.SpecialtyIds[2],
            _ => _data.SpecialtyIds[0]
        };
        var before = await SnapshotAsync();
        using var response = await SendAsync(operation, target);
        await AssertProblemAsync(response, status, code);
        await AssertStateAsync(before);
    }

    [Fact]
    public async Task Puede_retirar_una_especialidad_inactiva_si_conserva_otra()
    {
        await PrepareAsync();
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.specialty SET is_active = false WHERE id = {_data.SpecialtyIds[0]}"));
        await AuthenticateAsync(_a);
        var before = await SnapshotAsync();
        using var response = await SendAsync("DELETE", _data.SpecialtyIds[0]);
        await AssertSuccessAsync(response);
        await AssertStateAsync(before, "DELETE", _data.SpecialtyIds[0]);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Anonimo_no_puede_modificar_asociaciones(string operation)
    {
        await PrepareAsync();
        var before = await SnapshotAsync();
        using var response = await SendAsync(operation, _data.SpecialtyIds[0]);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await AssertStateAsync(before);
    }

    [Theory]
    [InlineData("POST", UserRole.Patient)]
    [InlineData("DELETE", UserRole.Patient)]
    [InlineData("POST", UserRole.Administrator)]
    [InlineData("DELETE", UserRole.Administrator)]
    public async Task Otros_roles_con_login_y_sin_pasos_pendientes_no_tienen_permiso(string operation, UserRole role)
    {
        await PrepareAsync();
        var user = await CreateUserAsync(role);
        await WithContextAsync(context => InsertProfileAsync(context, user));
        await AuthenticateAsync(user);
        var before = await SnapshotAsync();
        using var response = await SendAsync(operation, _data.SpecialtyIds[0]);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await AssertStateAsync(before);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Doctor_sin_perfil_es_bloqueado_por_su_paso_pendiente_real(string operation)
    {
        await PrepareAsync();
        var user = await CreateUserAsync(UserRole.Doctor);
        await AuthenticateAsync(user, PendingStepClaimValues.DoctorProfile);
        var before = await SnapshotAsync();
        using var response = await SendAsync(operation, _data.SpecialtyIds[0]);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await AssertStateAsync(before);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Perfil_legado_vacio_llega_al_handler_y_devuelve_ProfileIncomplete(string operation)
    {
        await PrepareAsync();
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM people.doctor_specialty WHERE doctor_id = {_data.DoctorAId}"));
        await AuthenticateAsync(_a);
        var before = await SnapshotAsync();
        using var response = await SendAsync(operation, _data.SpecialtyIds[0]);
        await AssertProblemAsync(response, 409, "Doctor.ProfileIncomplete");
        await AssertStateAsync(before);
    }

    [Theory]
    [InlineData("POST", "query")]
    [InlineData("DELETE", "query")]
    [InlineData("POST", "body")]
    [InlineData("DELETE", "body")]
    [InlineData("POST", "headers")]
    [InlineData("DELETE", "headers")]
    public async Task IDs_ajenos_del_cliente_no_seleccionan_al_propietario(string operation, string source)
    {
        await PrepareAsync();
        await AuthenticateAsync(_a);
        var target = _data.SpecialtyIds[operation == "POST" ? 2 : 0];
        var before = await SnapshotAsync();
        var url = Url(target);
        if (source == "query") url += $"?doctorId={_data.DoctorBId}&userId={_b.Id}";
        using var request = new HttpRequestMessage(new HttpMethod(operation), url);
        if (source == "body") request.Content = JsonContent.Create(new { doctorId = _data.DoctorBId, userId = _b.Id });
        if (source == "headers")
        {
            request.Headers.Add("DoctorId", _data.DoctorBId.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("UserId", _b.Id.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Doctor-Id", _data.DoctorBId.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-User-Id", _b.Id.ToString(CultureInfo.InvariantCulture));
        }
        using var response = await Client.SendAsync(request);
        await AssertSuccessAsync(response);
        await AssertStateAsync(before, operation, target);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task No_existe_ruta_para_elegir_otro_Doctor_o_User(string operation)
    {
        await PrepareAsync();
        await AuthenticateAsync(_a);
        var before = await SnapshotAsync();
        foreach (var ownerId in new[] { _data.DoctorBId, _b.Id })
        {
            using var request = new HttpRequestMessage(new HttpMethod(operation),
                $"/api/doctors/{ownerId}/specialties/{_data.SpecialtyIds[0]}");
            using var response = await Client.SendAsync(request);
            await response.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
            await AssertStateAsync(before);
        }
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("confirmed")]
    public async Task Desasociar_conserva_todos_los_campos_de_la_cita_previamente_agendada(string status)
    {
        await PrepareAsync();
        var patient = await CreateUserAsync(UserRole.Patient);
        int appointmentId;
        int patientId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            appointmentId = await InsertAppointmentAsync(context,
                patient.Id, _data.DoctorAId, _data.SpecialtyIds[0], status);
            patientId = await context.Patients.Where(value => value.UserId == patient.Id)
                .Select(value => value.Id).SingleAsync();
        }
        await AuthenticateAsync(_a);
        var before = await SnapshotAsync();
        before.Links.Where(value => Field(value, "doctor_id") == _data.DoctorAId)
            .Select(value => Field(value, "specialty_id")).ShouldBe(_data.SpecialtyIds.Take(2));
        before.Appointments.ShouldHaveSingleItem();
        using (var appointment = JsonDocument.Parse(before.Appointments[0]))
        {
            var row = appointment.RootElement;
            row.GetProperty("id").GetInt32().ShouldBe(appointmentId);
            row.GetProperty("patient_id").GetInt32().ShouldBe(patientId);
            row.GetProperty("doctor_id").GetInt32().ShouldBe(_data.DoctorAId);
            row.GetProperty("specialty_id").GetInt32().ShouldBe(_data.SpecialtyIds[0]);
            row.GetProperty("status").GetString().ShouldBe(status);
            row.GetProperty("reason_for_visit").GetString().ShouldBe("Consulta previamente agendada");
            row.GetProperty("status_reason").GetString().ShouldBe("Reserva de prueba");
            row.GetProperty("created_by").GetInt32().ShouldBe(patient.Id);
            row.GetProperty("ends_at").GetDateTimeOffset().ShouldBeGreaterThan(row.GetProperty("starts_at").GetDateTimeOffset());
            before.Availability.ShouldHaveSingleItem();
            using var block = JsonDocument.Parse(before.Availability[0]);
            block.RootElement.GetProperty("doctor_id").GetInt32().ShouldBe(_data.DoctorAId);
            block.RootElement.GetProperty("series_id").ValueKind.ShouldBe(JsonValueKind.Null);
            block.RootElement.GetProperty("is_available").GetBoolean().ShouldBeTrue();
            block.RootElement.GetProperty("starts_at").GetDateTimeOffset()
                .ShouldBeLessThanOrEqualTo(row.GetProperty("starts_at").GetDateTimeOffset());
            block.RootElement.GetProperty("ends_at").GetDateTimeOffset()
                .ShouldBeGreaterThanOrEqualTo(row.GetProperty("ends_at").GetDateTimeOffset());
        }
        using var response = await SendAsync("DELETE", _data.SpecialtyIds[0]);
        await AssertSuccessAsync(response);
        // AssertState compara todo el JSON persistido, incluidos motivos y auditoría.
        await AssertStateAsync(before, "DELETE", _data.SpecialtyIds[0]);
        await AssertResetPreservesGeographyAsync();
    }

    private async Task AssertResetPreservesGeographyAsync()
    {
        async Task<string[]> GeographyAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.SqlQuery<string>(
                $"""
                SELECT jsonb_build_object('province', p)::text AS "Value" FROM people.province p
                UNION ALL
                SELECT jsonb_build_object('municipality', m)::text AS "Value" FROM people.municipality m
                ORDER BY "Value"
                """).ToListAsync()).ToArray();
        }

        var geography = await GeographyAsync();
        geography.ShouldNotBeEmpty();
        // Usa el reinicio existente: prueba el orden con citas activas y su idempotencia.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await SharedApp.ResetAsync();
            await WithContextAsync(async context =>
            {
                var remaining = await context.Database.SqlQuery<long>(
                    $"""
                    SELECT (
                        (SELECT count(*) FROM app."user") + (SELECT count(*) FROM people.doctor)
                        + (SELECT count(*) FROM people.patient) + (SELECT count(*) FROM people.specialty)
                        + (SELECT count(*) FROM people.doctor_specialty)
                        + (SELECT count(*) FROM scheduling.appointment)
                        + (SELECT count(*) FROM scheduling.doctor_availability)
                    ) AS "Value"
                    """).SingleAsync();
                remaining.ShouldBe(0);
            });
            (await GeographyAsync()).ShouldBe(geography);
        }
    }

    private async Task PrepareAsync()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        _a = await CreateUserAsync(UserRole.Doctor);
        _b = await CreateUserAsync(UserRole.Doctor);
        await using var scope = Factory.Services.CreateAsyncScope();
        _data = await InsertDoctorSpecialtyDataAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _a.Id, _b.Id);
        _data.DoctorAId.ShouldNotBe(_a.Id);
        _data.DoctorBId.ShouldNotBe(_b.Id);
        _a.Id.ShouldNotBe(_b.Id);
        _data.DoctorAId.ShouldNotBe(_data.DoctorBId);
        new[] { _a.Id, _b.Id, _data.DoctorAId, _data.DoctorBId }.Distinct().Count().ShouldBe(4);
    }

    private async Task<User> CreateUserAsync(UserRole role)
    {
        var email = $"doctor-specialty-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(role, email, Password, mustChangePassword: false);
        return (await FindUserAsync(email))!;
    }

    private async Task AuthenticateAsync(User user, string? pendingStep = null)
    {
        using var response = await LoginAsync(user.Email.Value, Password);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var login = await response.ReadLoginAsync();
        login.RequiresTwoFactor.ShouldBeFalse();
        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        var jwt = new JsonWebToken(login.AccessToken);
        jwt.Claims.Where(claim => claim.Type == AppClaims.UserId).Select(claim => claim.Value)
            .ShouldBe(new[] { user.Id.ToString(CultureInfo.InvariantCulture) });
        jwt.Claims.Where(claim => claim.Type == AppClaims.Role).Select(claim => claim.Value)
            .ShouldBe(new[] { user.Role.ToString() });
        jwt.Claims.Where(claim => claim.Type == AppClaims.PendingStep).Select(claim => claim.Value)
            .ShouldBe(pendingStep is null ? Array.Empty<string>() : [pendingStep]);
        if (pendingStep is null) login.PendingStep.ShouldBeNull();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private static string Url(int id) => $"/api/doctors/me/specialties/{id}";

    private async Task<HttpResponseMessage> SendAsync(string operation, int id)
    {
        using var request = new HttpRequestMessage(new HttpMethod(operation), Url(id));
        return await Client.SendAsync(request);
    }

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<DoctorSpecialtySnapshot> SnapshotAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await ReadDoctorSpecialtySnapshotAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _data.DoctorIds);
    }

    private async Task AssertStateAsync(DoctorSpecialtySnapshot before, string? operation = null, int target = 0)
    {
        var after = await SnapshotAsync();
        after.Doctors.ShouldBe(before.Doctors);
        after.Specialties.ShouldBe(before.Specialties);
        after.Appointments.ShouldBe(before.Appointments);
        after.Availability.ShouldBe(before.Availability);
        bool IsTarget(string row) => Field(row, "doctor_id") == _data.DoctorAId && Field(row, "specialty_id") == target;
        if (operation == "POST")
        {
            before.Links.Where(IsTarget).ShouldBeEmpty();
            after.Links.Where(IsTarget).ShouldHaveSingleItem();
            after.Links.Where(row => !IsTarget(row)).ShouldBe(before.Links);
        }
        else if (operation == "DELETE")
        {
            before.Links.Where(IsTarget).ShouldHaveSingleItem();
            after.Links.ShouldBe(before.Links.Where(row => !IsTarget(row)));
        }
        else after.Links.ShouldBe(before.Links);
        var expectedIds = before.Links.Where(row => Field(row, "doctor_id") == _data.DoctorAId)
            .Select(row => Field(row, "specialty_id"));
        if (operation == "POST") expectedIds = expectedIds.Append(target);
        if (operation == "DELETE") expectedIds = expectedIds.Where(id => id != target);
        after.Links.Where(row => Field(row, "doctor_id") == _data.DoctorAId)
            .Select(row => Field(row, "specialty_id")).ShouldBe(expectedIds.Order());
        after.Links.Where(row => Field(row, "doctor_id") == _data.DoctorBId)
            .ShouldBe(before.Links.Where(row => Field(row, "doctor_id") == _data.DoctorBId));
    }

    private static int Field(string row, string name)
    {
        using var document = JsonDocument.Parse(row);
        return document.RootElement.GetProperty(name).GetInt32();
    }

    private static async Task AssertSuccessAsync(HttpResponseMessage response)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        await response.ShouldHaveStatusAsync((HttpStatusCode)status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().ShouldBe(status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
