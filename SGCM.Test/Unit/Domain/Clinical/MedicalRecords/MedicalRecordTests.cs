using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common.Exceptions;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Clinical.MedicalRecords;

/// <summary>
/// Pruebas de <see cref="MedicalRecord"/>: crear una versión, corregir creando otra sin tocar la existente, y
/// elegir la vigente como la más reciente.
/// </summary>
public sealed class MedicalRecordTests
{
    private const int AppointmentId = 10;
    private const int PatientId = 20;
    private const int DoctorUserId = 30;

    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 2, 14, 30, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Se_crea_una_version_con_el_contenido_la_cita_y_quien_la_registra()
    {
        var content = Content("Faringitis", "Reposo", "Control en 7 días");

        var record = MedicalRecord.Create(AppointmentId, PatientId, content, DoctorUserId, RecordedAt);

        record.AppointmentId.ShouldBe(AppointmentId);
        record.PatientId.ShouldBe(PatientId);
        record.Diagnosis.ShouldBe("Faringitis");
        record.Treatment.ShouldBe("Reposo");
        record.Notes.ShouldBe("Control en 7 días");
        record.CreatedBy.ShouldBe(DoctorUserId);
        record.CreatedAt.ShouldBe(RecordedAt);
        record.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        record.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, PatientId, DoctorUserId)]
    [InlineData(AppointmentId, 0, DoctorUserId)]
    [InlineData(AppointmentId, PatientId, 0)]
    public void Identificadores_invalidos_son_un_error_de_programacion(int appointmentId, int patientId, int createdBy)
    {
        Should.Throw<DomainException>(() =>
            MedicalRecord.Create(appointmentId, patientId, Content("Faringitis"), createdBy, RecordedAt));
    }

    [Fact]
    public void Corregir_crea_una_version_nueva_de_la_misma_cita_y_el_mismo_paciente()
    {
        var original = MedicalRecord.Create(AppointmentId, PatientId, Content("Faringitis"), DoctorUserId, RecordedAt);
        var correctedAt = RecordedAt.AddHours(1);

        var corrected = original.Correct(Content("Amigdalitis", "Antibiótico"), DoctorUserId, correctedAt);

        corrected.ShouldNotBeSameAs(original);
        corrected.AppointmentId.ShouldBe(AppointmentId);
        corrected.PatientId.ShouldBe(PatientId);
        corrected.Diagnosis.ShouldBe("Amigdalitis");
        corrected.Treatment.ShouldBe("Antibiótico");
        corrected.Notes.ShouldBeNull();
        corrected.CreatedAt.ShouldBe(correctedAt);
    }

    [Fact]
    public void Corregir_no_modifica_la_version_existente()
    {
        var original = MedicalRecord.Create(
            AppointmentId, PatientId, Content("Faringitis", "Reposo", "Control"), DoctorUserId, RecordedAt);

        original.Correct(Content("Amigdalitis"), DoctorUserId, RecordedAt.AddHours(1));

        original.Diagnosis.ShouldBe("Faringitis");
        original.Treatment.ShouldBe("Reposo");
        original.Notes.ShouldBe("Control");
        original.CreatedAt.ShouldBe(RecordedAt);
    }

    [Fact]
    public void Una_correccion_en_el_mismo_instante_se_acepta()
    {
        var original = MedicalRecord.Create(AppointmentId, PatientId, Content("Faringitis"), DoctorUserId, RecordedAt);

        original.Correct(Content("Amigdalitis"), DoctorUserId, RecordedAt).CreatedAt.ShouldBe(RecordedAt);
    }

    [Fact]
    public void Una_correccion_anterior_a_la_version_corregida_es_un_error_de_programacion()
    {
        var original = MedicalRecord.Create(AppointmentId, PatientId, Content("Faringitis"), DoctorUserId, RecordedAt);

        Should.Throw<DomainException>(() =>
            original.Correct(Content("Amigdalitis"), DoctorUserId, RecordedAt.AddSeconds(-1)));
    }

    [Fact]
    public void La_vigente_es_la_version_mas_reciente_sin_importar_el_orden_recibido()
    {
        var first = MedicalRecord.Create(AppointmentId, PatientId, Content("Faringitis"), DoctorUserId, RecordedAt);
        var second = first.Correct(Content("Amigdalitis"), DoctorUserId, RecordedAt.AddHours(1));
        var third = second.Correct(Content("Amigdalitis bacteriana"), DoctorUserId, RecordedAt.AddHours(2));

        MedicalRecord.Current([second, third, first]).ShouldBeSameAs(third);
    }

    [Fact]
    public void Una_cita_sin_versiones_no_tiene_registro_vigente()
    {
        MedicalRecord.Current([]).ShouldBeNull();
    }

    [Fact]
    public void Versiones_de_citas_distintas_son_un_error_de_programacion()
    {
        var record = MedicalRecord.Create(AppointmentId, PatientId, Content("Faringitis"), DoctorUserId, RecordedAt);
        var otherAppointment = MedicalRecord.Create(
            AppointmentId + 1, PatientId, Content("Gripe"), DoctorUserId, RecordedAt);

        Should.Throw<DomainException>(() => MedicalRecord.Current([record, otherAppointment]));
    }

    private static MedicalRecordContent Content(string? diagnosis, string? treatment = null, string? notes = null) =>
        MedicalRecordContent.Create(diagnosis, treatment, notes).Value;
}
