using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Clinical.MedicalHistory;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Test.Integration.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Clinical;

/// <summary>
/// Pruebas de integración del historial médico contra la API y la base reales: qué ve el paciente, qué ve el médico
/// tratante, quién no puede consultarlo y el registro de accesos. Las citas se siembran en el pasado, relativas a la
/// hora en punto actual, y las versiones del registro se guardan directamente con sus horas de creación.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MedicalHistoryTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string PatientHistoryUrl = "/api/patients/me/medical-history";
    private const string DoctorPatientsUrl = "/api/doctors/me/patients";

    [Fact]
    public async Task El_paciente_ve_la_version_vigente_de_sus_citas_atendidas_y_las_que_no_tienen_registro()
    {
        var history = await SeedHistoryAsync();

        using var response = await history.Patient.Client.GetAsync(PatientHistoryUrl);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("patientId").GetInt32().ShouldBe(history.Patient.PatientId);
        var entries = Entries(body);
        entries.Select(entry => entry.GetProperty("appointmentId").GetInt32())
            .ShouldBe([history.OwnAppointmentId, history.OtherAppointmentId, history.UnrecordedAppointmentId]);
        AssertCurrent(entries[0], history.OwnCurrent.Id, "Amigdalitis");
        entries[0].GetProperty("status").GetString().ShouldBe(Completed);
        entries[0].GetProperty("previousVersions").GetArrayLength().ShouldBe(0);
        AssertCurrent(entries[1], history.OtherCurrent.Id, "Influenza");
        entries[1].GetProperty("previousVersions").GetArrayLength().ShouldBe(0);
        entries[2].GetProperty("hasMedicalRecord").GetBoolean().ShouldBeFalse();
        entries[2].GetProperty("currentVersion").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task El_historial_del_paciente_trae_el_medico_y_la_especialidad_de_cada_cita()
    {
        var history = await SeedHistoryAsync();

        using var response = await history.Patient.Client.GetAsync(PatientHistoryUrl);

        var entry = Entries(await response.Content.ReadFromJsonAsync<JsonElement>())[0];
        entry.GetProperty("doctorId").GetInt32().ShouldBe(history.Doctor.DoctorId);
        entry.GetProperty("doctorName").GetString().ShouldNotBeNullOrWhiteSpace();
        entry.GetProperty("specialtyName").GetString().ShouldNotBeNullOrWhiteSpace();
        entry.GetProperty("currentVersion").GetProperty("createdAt").GetDateTimeOffset()
            .ShouldBe(history.OwnCurrent.CreatedAt);
    }

    [Fact]
    public async Task La_consulta_del_paciente_deja_una_fila_view_por_cada_version_mostrada()
    {
        var history = await SeedHistoryAsync();

        using var response = await history.Patient.Client.GetAsync(PatientHistoryUrl);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var patientUserId = (await FindUserAsync(history.Patient.Email))!.Id;
        (await ReadViewsAsync(patientUserId)).ShouldBe([history.OwnCurrent.Id, history.OtherCurrent.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task El_medico_tratante_ve_las_versiones_anteriores_solo_de_las_citas_que_atendio()
    {
        var history = await SeedHistoryAsync();

        using var response = await GetAsDoctorAsync(history.Doctor, history.Patient.PatientId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var entries = Entries(await response.Content.ReadFromJsonAsync<JsonElement>());
        AssertCurrent(entries[0], history.OwnCurrent.Id, "Amigdalitis");
        entries[0].GetProperty("previousVersions").EnumerateArray()
            .Select(version => version.GetProperty("id").GetInt32()).ShouldBe([history.OwnPrevious.Id]);
        AssertCurrent(entries[1], history.OtherCurrent.Id, "Influenza");
        entries[1].GetProperty("previousVersions").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task La_consulta_del_medico_registra_tambien_las_versiones_anteriores_que_ve()
    {
        var history = await SeedHistoryAsync();

        using var response = await GetAsDoctorAsync(history.Doctor, history.Patient.PatientId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var doctorUserId = (await FindUserAsync(history.Doctor.Email))!.Id;
        (await ReadViewsAsync(doctorUserId)).ShouldBe(
            [history.OwnCurrent.Id, history.OwnPrevious.Id, history.OtherCurrent.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Un_medico_con_solo_una_cita_pendiente_con_el_paciente_tambien_puede_consultarlo()
    {
        var history = await SeedHistoryAsync();
        var newDoctor = await SeedDoctorAsync();
        // Un día después de la otra cita pendiente del paciente, para no cruzarse con ella.
        await SeedAppointmentAsync(newDoctor, history.Patient, Pending, InTimeStart().AddDays(1));

        using var response = await GetAsDoctorAsync(newDoctor, history.Patient.PatientId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var entries = Entries(await response.Content.ReadFromJsonAsync<JsonElement>());
        entries.Count.ShouldBe(3);
        entries.ShouldAllBe(entry => entry.GetProperty("previousVersions").GetArrayLength() == 0);
    }

    [Fact]
    public async Task Un_medico_sin_citas_con_el_paciente_recibe_403_y_no_deja_accesos()
    {
        var history = await SeedHistoryAsync();
        var stranger = await SeedDoctorAsync();

        using var response = await GetAsDoctorAsync(stranger, history.Patient.PatientId);

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, MedicalHistoryErrors.NoAppointmentWithPatient.Code);
        (await ReadViewsAsync((await FindUserAsync(stranger.Email))!.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_paciente_inexistente_responde_igual_que_uno_sin_relacion()
    {
        var doctor = await SeedDoctorAsync();

        using var response = await GetAsDoctorAsync(doctor, int.MaxValue);

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, MedicalHistoryErrors.NoAppointmentWithPatient.Code);
    }

    [Fact]
    public async Task Un_id_de_paciente_que_no_es_mayor_que_cero_responde_400()
    {
        var doctor = await SeedDoctorAsync();

        using var response = await GetAsDoctorAsync(doctor, 0);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "MedicalHistory.InvalidPatientId");
    }

    [Fact]
    public async Task Un_paciente_sin_citas_en_el_historial_recibe_una_lista_vacia_y_no_deja_accesos()
    {
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.GetAsync(PatientHistoryUrl);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        Entries(await response.Content.ReadFromJsonAsync<JsonElement>()).ShouldBeEmpty();
        (await ReadViewsAsync((await FindUserAsync(patient.Email))!.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cada_rol_solo_usa_su_endpoint()
    {
        var history = await SeedHistoryAsync();

        using var doctorOnPatientEndpoint = await history.Doctor.Client.GetAsync(PatientHistoryUrl);
        using var patientOnDoctorEndpoint = await history.Patient.Client.GetAsync(DoctorHistoryUrl(history.Patient.PatientId));

        await doctorOnPatientEndpoint.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await patientOnDoctorEndpoint.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sin_token_ambos_endpoints_responden_401()
    {
        using var anonymous = Factory.CreateClient();

        using var patientResponse = await anonymous.GetAsync(PatientHistoryUrl);
        using var doctorResponse = await anonymous.GetAsync(DoctorHistoryUrl(1));

        await patientResponse.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await doctorResponse.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Un paciente con tres citas pasadas: una completada con el médico de la prueba y un registro corregido, otra con
    /// otro médico y su registro corregido, y una completada sin registro. Tiene además una cita pendiente futura,
    /// que no entra en el historial.
    /// </summary>
    private async Task<SeededHistory> SeedHistoryAsync()
    {
        var doctor = await SeedDoctorAsync();
        var otherDoctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var ownStart = CurrentHourUtc().AddDays(-3);
        var otherStart = CurrentHourUtc().AddDays(-10);
        var unrecordedStart = CurrentHourUtc().AddDays(-20);

        var ownId = await SeedAppointmentAsync(doctor, patient, Completed, ownStart);
        var otherId = await SeedAppointmentAsync(otherDoctor, patient, Completed, otherStart);
        var unrecordedId = await SeedAppointmentAsync(otherDoctor, patient, Completed, unrecordedStart);
        await SeedAppointmentAsync(doctor, patient, Pending, InTimeStart());

        var ownPrevious = await AddVersionAsync(ownId, patient, doctor, "Faringitis", ownStart.AddHours(1));
        var ownCurrent = await AddVersionAsync(ownId, patient, doctor, "Amigdalitis", ownStart.AddHours(3));
        await AddVersionAsync(otherId, patient, otherDoctor, "Gripe", otherStart.AddHours(1));
        var otherCurrent = await AddVersionAsync(otherId, patient, otherDoctor, "Influenza", otherStart.AddHours(2));

        return new SeededHistory(
            doctor, patient, ownId, otherId, unrecordedId, ownPrevious, ownCurrent, otherCurrent);
    }

    private async Task<int> SeedAppointmentAsync(
        DoctorSeed doctor, PatientSeed patient, string status, DateTimeOffset startsAtUtc)
    {
        await InsertBlockAsync(
            doctor.DoctorId, startsAtUtc.AddHours(-BlockMarginHours), startsAtUtc.AddHours(AppointmentHours + BlockMarginHours));
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status));
        return appointmentId;
    }

    private async Task<MedicalRecord> AddVersionAsync(
        int appointmentId, PatientSeed patient, DoctorSeed doctor, string diagnosis, DateTimeOffset createdAt)
    {
        var doctorUserId = (await FindUserAsync(doctor.Email))!.Id;
        var record = MedicalRecord.Create(
            appointmentId, patient.PatientId, MedicalRecordContent.Create(diagnosis, null, null).Value, doctorUserId,
            createdAt);
        await WithContextAsync(async context =>
        {
            context.Add(record);
            await context.SaveChangesAsync();
        });
        return record;
    }

    private static Task<HttpResponseMessage> GetAsDoctorAsync(DoctorSeed doctor, int patientId) =>
        doctor.Client.GetAsync(DoctorHistoryUrl(patientId));

    private static string DoctorHistoryUrl(int patientId) => $"{DoctorPatientsUrl}/{patientId}/medical-history";

    private static List<JsonElement> Entries(JsonElement body) => body.GetProperty("entries").EnumerateArray().ToList();

    private static void AssertCurrent(JsonElement entry, int versionId, string diagnosis)
    {
        entry.GetProperty("hasMedicalRecord").GetBoolean().ShouldBeTrue();
        var current = entry.GetProperty("currentVersion");
        current.GetProperty("id").GetInt32().ShouldBe(versionId);
        current.GetProperty("diagnosis").GetString().ShouldBe(diagnosis);
    }

    private async Task<List<int>> ReadViewsAsync(int userId)
    {
        List<int> ids = [];
        await WithContextAsync(async context => ids = await context.Database.SqlQuery<int>(
            $"""
            SELECT medical_record_id AS "Value"
            FROM clinical.medical_record_access_log
            WHERE accessed_by = {userId} AND access_type = 'view'
            """).ToListAsync());
        return ids;
    }

    private sealed record SeededHistory(
        DoctorSeed Doctor,
        PatientSeed Patient,
        int OwnAppointmentId,
        int OtherAppointmentId,
        int UnrecordedAppointmentId,
        MedicalRecord OwnPrevious,
        MedicalRecord OwnCurrent,
        MedicalRecord OtherCurrent);
}
