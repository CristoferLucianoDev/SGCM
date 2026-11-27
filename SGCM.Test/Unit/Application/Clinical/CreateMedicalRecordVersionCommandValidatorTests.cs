using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using Shouldly;

namespace SGCM.Test.Unit.Application.Clinical;

/// <summary>Pruebas de <see cref="CreateMedicalRecordVersionCommandValidator"/>.</summary>
public sealed class CreateMedicalRecordVersionCommandValidatorTests
{
    private readonly CreateMedicalRecordVersionCommandValidator _validator = new();

    [Fact]
    public void Acepta_una_sola_parte_escrita()
    {
        _validator.Validate(Command(notes: "Paciente estable")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Acepta_las_longitudes_maximas_despues_de_recortar()
    {
        var command = Command(
            $"  {Text(MedicalRecordContent.DiagnosisMaxLength)}  ",
            Text(MedicalRecordContent.TreatmentMaxLength),
            Text(MedicalRecordContent.NotesMaxLength));

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_de_la_cita_debe_ser_mayor_que_cero(int appointmentId)
    {
        AssertSingleError(Command("Faringitis") with { AppointmentId = appointmentId }, "Appointment.InvalidAppointmentId");
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(" ", "", "\t")]
    public void Sin_ninguna_parte_escrita_es_un_registro_vacio(string? diagnosis, string? treatment, string? notes)
    {
        AssertSingleError(Command(diagnosis, treatment, notes), MedicalRecordErrors.Empty.Code);
    }

    [Fact]
    public void Un_diagnostico_demasiado_largo_se_rechaza()
    {
        AssertSingleError(
            Command(Text(MedicalRecordContent.DiagnosisMaxLength + 1)), MedicalRecordErrors.DiagnosisTooLong.Code);
    }

    [Fact]
    public void Un_tratamiento_demasiado_largo_se_rechaza()
    {
        AssertSingleError(
            Command(treatment: Text(MedicalRecordContent.TreatmentMaxLength + 1)), MedicalRecordErrors.TreatmentTooLong.Code);
    }

    [Fact]
    public void Unas_notas_demasiado_largas_se_rechazan()
    {
        AssertSingleError(
            Command(notes: Text(MedicalRecordContent.NotesMaxLength + 1)), MedicalRecordErrors.NotesTooLong.Code);
    }

    private void AssertSingleError(CreateMedicalRecordVersionCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }

    private static CreateMedicalRecordVersionCommand Command(
        string? diagnosis = null, string? treatment = null, string? notes = null) =>
        new(AppointmentId: 99, diagnosis, treatment, notes, IpAddress: "10.0.0.5");

    private static string Text(int length) => new('a', length);
}
