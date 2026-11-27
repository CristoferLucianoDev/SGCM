using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Test.Integration.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Clinical;

/// <summary>
/// Pruebas de integración del registro de información clínica contra la API y la base reales: crear la primera
/// versión, corregir con una versión nueva, el registro de accesos y los rechazos. El reloj de la app no se mueve (la
/// sesión del médico se creó con la hora real) y las citas se siembran relativas a la hora en punto actual.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CreateMedicalRecordVersionTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const string Diagnosis = "Faringitis aguda";
    private const string Treatment = "Reposo e hidratación";
    private const string Notes = "Control en 7 días";
    private const int HoursSinceStartWhenInProgress = 1;
    private const int HoursSinceStartWhenFinished = 3;

    [Fact]
    public async Task La_primera_vez_crea_el_registro_y_deja_constancia_de_la_creacion()
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());

        using var response = await PostAsync(doctor.Client, appointmentId, new { diagnosis = $"  {Diagnosis} ", treatment = Treatment });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetInt32();
        body.GetProperty("appointmentId").GetInt32().ShouldBe(appointmentId);
        body.GetProperty("diagnosis").GetString().ShouldBe(Diagnosis);
        body.GetProperty("treatment").GetString().ShouldBe(Treatment);
        body.GetProperty("notes").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("isCorrection").GetBoolean().ShouldBeFalse();
        var doctorUserId = (await FindUserAsync(doctor.Email))!.Id;
        (await ReadVersionsAsync(appointmentId)).ShouldBe(
        [
            new VersionRow(id, patient.PatientId, Diagnosis, Treatment, null, doctorUserId)
        ]);
        (await ReadAccessesAsync(appointmentId)).ShouldBe([new AccessRow(id, doctorUserId, "create")]);
    }

    [Fact]
    public async Task Corregir_crea_una_version_nueva_vigente_y_conserva_la_anterior_sin_cambios()
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());
        var firstId = await CreateAsync(doctor.Client, appointmentId, new { diagnosis = Diagnosis });

        using var response = await PostAsync(doctor.Client, appointmentId, new { diagnosis = "Amigdalitis", notes = Notes });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var secondId = body.GetProperty("id").GetInt32();
        body.GetProperty("isCorrection").GetBoolean().ShouldBeTrue();
        var doctorUserId = (await FindUserAsync(doctor.Email))!.Id;
        (await ReadVersionsAsync(appointmentId)).ShouldBe(
        [
            new VersionRow(firstId, patient.PatientId, Diagnosis, null, null, doctorUserId),
            new VersionRow(secondId, patient.PatientId, "Amigdalitis", null, Notes, doctorUserId)
        ]);
        (await ReadCurrentIdAsync(appointmentId)).ShouldBe(secondId);
        (await ReadAccessesAsync(appointmentId)).ShouldBe(
        [
            new AccessRow(firstId, doctorUserId, "create"),
            new AccessRow(secondId, doctorUserId, "update")
        ]);
    }

    [Fact]
    public async Task Una_cita_completada_tambien_admite_registro()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Completed, FinishedStart());

        using var response = await PostAsync(doctor.Client, appointmentId, new { notes = Notes });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await ReadVersionsAsync(appointmentId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Una_cita_confirmada_que_ya_termino_sin_completarse_admite_registro()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, FinishedStart());

        using var response = await PostAsync(doctor.Client, appointmentId, new { diagnosis = Diagnosis });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Una_cita_que_aun_no_empieza_responde_409_y_no_registra_nada()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());

        using var response = await PostAsync(doctor.Client, appointmentId, new { diagnosis = Diagnosis });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, AppointmentErrors.ClinicalRecordNotStarted.Code);
        await AssertNothingRecordedAsync(appointmentId);
    }

    [Theory]
    [InlineData(Pending, null)]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    public async Task Una_cita_que_no_esta_confirmada_ni_completada_responde_409_y_no_registra_nada(
        string status, string? statusReason)
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(status, InProgressStart(), statusReason);

        using var response = await PostAsync(doctor.Client, appointmentId, new { diagnosis = Diagnosis });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, AppointmentErrors.ClinicalRecordInvalidStatus.Code);
        await AssertNothingRecordedAsync(appointmentId);
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_404_y_no_registra_nada()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());
        var otherDoctor = await SeedDoctorAsync();

        using var response = await PostAsync(otherDoctor.Client, appointmentId, new { diagnosis = Diagnosis });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, AppointmentErrors.NotFound.Code);
        await AssertNothingRecordedAsync(appointmentId);
    }

    [Fact]
    public async Task Sin_contenido_responde_400_y_no_registra_nada()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());

        using var response = await PostAsync(doctor.Client, appointmentId, new { diagnosis = "  ", notes = "" });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, MedicalRecordErrors.Empty.Code);
        await AssertNothingRecordedAsync(appointmentId);
    }

    [Fact]
    public async Task Unas_notas_mas_largas_que_el_modelo_responden_400_y_no_registran_nada()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());

        using var response = await PostAsync(
            doctor.Client, appointmentId, new { notes = new string('a', MedicalRecordContent.NotesMaxLength + 1) });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, MedicalRecordErrors.NotesTooLong.Code);
        await AssertNothingRecordedAsync(appointmentId);
    }

    [Fact]
    public async Task Un_id_que_no_es_mayor_que_cero_responde_400()
    {
        var doctor = await SeedDoctorAsync();

        using var response = await PostAsync(doctor.Client, 0, new { diagnosis = Diagnosis });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.InvalidAppointmentId");
    }

    [Fact]
    public async Task Un_paciente_autenticado_recibe_403_aunque_sea_su_cita()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());

        using var response = await PostAsync(patient.Client, appointmentId, new { diagnosis = Diagnosis });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await AssertNothingRecordedAsync(appointmentId);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());
        using var anonymous = Factory.CreateClient();

        using var response = await PostAsync(anonymous, appointmentId, new { diagnosis = Diagnosis });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await AssertNothingRecordedAsync(appointmentId);
    }

    /// <summary>Empieza hace una hora, relativo a la hora en punto actual del reloj de la app.</summary>
    private DateTimeOffset InProgressStart() => CurrentHourUtc().AddHours(-HoursSinceStartWhenInProgress);

    /// <summary>Empezó hace tres horas y terminó hace una, relativo a la hora en punto actual del reloj de la app.</summary>
    private DateTimeOffset FinishedStart() => CurrentHourUtc().AddHours(-HoursSinceStartWhenFinished);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, int appointmentId, object body) =>
        client.PostAsJsonAsync($"{DoctorAppointmentsUrl}/{appointmentId}/medical-record", body);

    private static async Task<int> CreateAsync(HttpClient client, int appointmentId, object body)
    {
        using var response = await PostAsync(client, appointmentId, body);
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task AssertNothingRecordedAsync(int appointmentId)
    {
        (await ReadVersionsAsync(appointmentId)).ShouldBeEmpty();
        (await ReadAccessesAsync(appointmentId)).ShouldBeEmpty();
    }

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private async Task<List<VersionRow>> ReadVersionsAsync(int appointmentId)
    {
        List<VersionRow> rows = [];
        await WithContextAsync(async context => rows = await context.Database.SqlQuery<VersionRow>(
            $"""
            SELECT id, patient_id, diagnosis, treatment, notes, created_by
            FROM clinical.medical_record
            WHERE appointment_id = {appointmentId}
            ORDER BY id
            """).ToListAsync());
        return rows;
    }

    private async Task<List<AccessRow>> ReadAccessesAsync(int appointmentId)
    {
        List<AccessRow> rows = [];
        await WithContextAsync(async context => rows = await context.Database.SqlQuery<AccessRow>(
            $"""
            SELECT log.medical_record_id, log.accessed_by, log.access_type
            FROM clinical.medical_record_access_log log
            JOIN clinical.medical_record record ON record.id = log.medical_record_id
            WHERE record.appointment_id = {appointmentId}
            ORDER BY log.id
            """).ToListAsync());
        return rows;
    }

    private async Task<int> ReadCurrentIdAsync(int appointmentId)
    {
        var id = 0;
        await WithContextAsync(async context => id = await context.Database.SqlQuery<int>(
            $"""
            SELECT id AS "Value" FROM clinical.vw_medical_record_current WHERE appointment_id = {appointmentId}
            """).SingleAsync());
        return id;
    }

    private sealed record VersionRow(
        int Id, int PatientId, string? Diagnosis, string? Treatment, string? Notes, int? CreatedBy);

    private sealed record AccessRow(int MedicalRecordId, int AccessedBy, string AccessType);
}
