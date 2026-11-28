using SGCM.Data.Domain.Clinical.MedicalHistory;
using SGCM.Data.Domain.Common.Exceptions;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Clinical.MedicalHistory;

/// <summary>
/// Pruebas de <see cref="MedicalHistoryAccess"/>: el paciente ve solo la versión vigente de cada cita, y el médico
/// tratante ve además las versiones anteriores de las citas que atendió.
/// </summary>
public sealed class MedicalHistoryAccessTests
{
    private const int PatientId = 20;
    private const int DoctorId = 3;
    private const int OtherDoctorId = 4;

    [Fact]
    public void El_paciente_consulta_su_historial_sin_ver_versiones_anteriores()
    {
        var access = MedicalHistoryAccess.ForPatient(PatientId);

        access.PatientId.ShouldBe(PatientId);
        access.DoctorId.ShouldBeNull();
        access.IsDoctor.ShouldBeFalse();
        access.ShowsPreviousVersions(DoctorId).ShouldBeFalse();
    }

    [Fact]
    public void El_medico_tratante_ve_las_versiones_anteriores_de_sus_citas()
    {
        var access = MedicalHistoryAccess.ForTreatingDoctor(DoctorId, PatientId);

        access.PatientId.ShouldBe(PatientId);
        access.DoctorId.ShouldBe(DoctorId);
        access.IsDoctor.ShouldBeTrue();
        access.ShowsPreviousVersions(DoctorId).ShouldBeTrue();
    }

    [Fact]
    public void El_medico_no_ve_las_versiones_anteriores_de_las_citas_de_otro_medico()
    {
        MedicalHistoryAccess.ForTreatingDoctor(DoctorId, PatientId).ShowsPreviousVersions(OtherDoctorId)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_paciente_invalido_es_un_error_de_programacion(int patientId)
    {
        Should.Throw<DomainException>(() => MedicalHistoryAccess.ForPatient(patientId));
        Should.Throw<DomainException>(() => MedicalHistoryAccess.ForTreatingDoctor(DoctorId, patientId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_medico_invalido_es_un_error_de_programacion(int doctorId)
    {
        Should.Throw<DomainException>(() => MedicalHistoryAccess.ForTreatingDoctor(doctorId, PatientId));
    }
}
