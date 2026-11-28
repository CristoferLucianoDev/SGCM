using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Clinical;
using SGCM.Data.Domain.Clinical.MedicalHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Patients;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Clinical;

/// <summary>
/// Pruebas de <see cref="MedicalHistoryAccessPolicy"/>: el paciente consulta solo su historial, y un médico el de un
/// paciente con quien tiene al menos una cita.
/// </summary>
public sealed class MedicalHistoryAccessPolicyTests
{
    private const int UserId = 42;
    private const int PatientId = 20;
    private const int DoctorId = 3;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorPatientRelationshipReader _relationships = Substitute.For<IDoctorPatientRelationshipReader>();
    private readonly MedicalHistoryAccessPolicy _policy;

    public MedicalHistoryAccessPolicyTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)DoctorId);
        _relationships.HasAppointmentWithPatientAsync(DoctorId, PatientId, Arg.Any<CancellationToken>()).Returns(true);
        _policy = new MedicalHistoryAccessPolicy(_currentUser, _patients, _doctors, _relationships);
    }

    [Fact]
    public async Task El_paciente_autenticado_consulta_su_propio_historial()
    {
        ActAs(UserRole.Patient);

        var result = await _policy.ForCurrentPatientAsync(CancellationToken.None);

        result.Value.ShouldBe(MedicalHistoryAccess.ForPatient(PatientId));
    }

    [Fact]
    public async Task Un_paciente_sin_perfil_se_rechaza()
    {
        ActAs(UserRole.Patient);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Patient?)null);

        (await _policy.ForCurrentPatientAsync(CancellationToken.None)).Error.ShouldBe(PatientErrors.ProfileNotFound);
    }

    [Theory]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public async Task Quien_no_es_paciente_no_consulta_un_historial_propio(UserRole role)
    {
        ActAs(role);

        (await _policy.ForCurrentPatientAsync(CancellationToken.None)).Error.ShouldBe(MedicalHistoryErrors.PatientOnly);
        _patients.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Sin_autenticar_no_se_consulta_ningun_historial()
    {
        _currentUser.IsAuthenticated.Returns(false);
        _currentUser.IsInRole(Arg.Any<UserRole>()).Returns(true);

        (await _policy.ForCurrentPatientAsync(CancellationToken.None)).Error.ShouldBe(MedicalHistoryErrors.PatientOnly);
        (await _policy.ForCurrentDoctorAsync(PatientId, CancellationToken.None)).Error
            .ShouldBe(MedicalHistoryErrors.DoctorOnly);
    }

    [Fact]
    public async Task Un_medico_con_una_cita_con_el_paciente_consulta_su_historial_como_tratante()
    {
        ActAs(UserRole.Doctor);

        var result = await _policy.ForCurrentDoctorAsync(PatientId, CancellationToken.None);

        result.Value.ShouldBe(MedicalHistoryAccess.ForTreatingDoctor(DoctorId, PatientId));
    }

    [Fact]
    public async Task Un_medico_sin_citas_con_el_paciente_se_rechaza()
    {
        ActAs(UserRole.Doctor);
        _relationships.HasAppointmentWithPatientAsync(DoctorId, PatientId, Arg.Any<CancellationToken>()).Returns(false);

        (await _policy.ForCurrentDoctorAsync(PatientId, CancellationToken.None)).Error
            .ShouldBe(MedicalHistoryErrors.NoAppointmentWithPatient);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Un_paciente_invalido_se_rechaza_igual_que_uno_sin_relacion_y_sin_consultar(int patientId)
    {
        ActAs(UserRole.Doctor);

        (await _policy.ForCurrentDoctorAsync(patientId, CancellationToken.None)).Error
            .ShouldBe(MedicalHistoryErrors.NoAppointmentWithPatient);
        _relationships.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_se_rechaza_sin_comprobar_la_relacion()
    {
        ActAs(UserRole.Doctor);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        (await _policy.ForCurrentDoctorAsync(PatientId, CancellationToken.None)).Error.ShouldBe(DoctorErrors.NotFound);
        _relationships.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Administrator)]
    public async Task Quien_no_es_medico_no_consulta_el_historial_de_un_paciente(UserRole role)
    {
        ActAs(role);

        (await _policy.ForCurrentDoctorAsync(PatientId, CancellationToken.None)).Error
            .ShouldBe(MedicalHistoryErrors.DoctorOnly);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _relationships.ReceivedCalls().ShouldBeEmpty();
    }

    private void ActAs(UserRole role) => _currentUser.IsInRole(Arg.Any<UserRole>()).Returns(call => call.Arg<UserRole>() == role);
}
