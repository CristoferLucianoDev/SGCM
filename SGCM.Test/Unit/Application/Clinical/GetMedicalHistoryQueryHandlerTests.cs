using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Clinical;
using SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;
using SGCM.Data.Domain.Clinical.MedicalHistory;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Clinical;

/// <summary>
/// Pruebas de <see cref="GetMedicalHistoryQueryHandler"/>: el paciente ve la versión vigente de cada cita, el médico
/// tratante ve además las anteriores de las citas que atendió, una cita completada sin registro se indica, y cada
/// versión mostrada queda en el registro de accesos.
/// </summary>
public sealed class GetMedicalHistoryQueryHandlerTests
{
    private const int UserId = 42;
    private const int PatientId = 20;
    private const int DoctorId = 3;
    private const int OtherDoctorId = 4;
    private const string IpAddress = "10.0.0.5";

    private static readonly DateTimeOffset StartsAt = new(2026, 10, 5, 14, 0, 0, TimeSpan.Zero);
    private static readonly MedicalHistoryVersionData OwnCurrent = Version(11, "Amigdalitis", StartsAt.AddHours(3));
    private static readonly MedicalHistoryVersionData OwnPrevious = Version(10, "Faringitis", StartsAt.AddHours(1));
    private static readonly MedicalHistoryVersionData OtherCurrent = Version(21, "Gripe", StartsAt.AddDays(-5));
    private static readonly MedicalHistoryVersionData OtherPrevious = Version(20, "Resfriado", StartsAt.AddDays(-6));

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorPatientRelationshipReader _relationships = Substitute.For<IDoctorPatientRelationshipReader>();
    private readonly IMedicalHistoryReader _reader = Substitute.For<IMedicalHistoryReader>();
    private readonly IMedicalRecordAccessLogRepository _accessLog = Substitute.For<IMedicalRecordAccessLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly GetMedicalHistoryQueryHandler _handler;

    public GetMedicalHistoryQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)DoctorId);
        _relationships.HasAppointmentWithPatientAsync(DoctorId, PatientId, Arg.Any<CancellationToken>()).Returns(true);
        _reader.ListAsync(PatientId, Arg.Any<CancellationToken>()).Returns(
        [
            Appointment(1, DoctorId, AppointmentStatus.Completed, OwnCurrent, OwnPrevious),
            Appointment(2, DoctorId, AppointmentStatus.Completed),
            Appointment(3, OtherDoctorId, AppointmentStatus.Confirmed, OtherCurrent, OtherPrevious)
        ]);

        var policy = new MedicalHistoryAccessPolicy(_currentUser, _patients, _doctors, _relationships);
        _handler = new GetMedicalHistoryQueryHandler(
            policy, _reader, _accessLog, _unitOfWork, _currentUser, _clock, new GetMedicalHistoryQueryValidator());
    }

    [Fact]
    public async Task El_paciente_ve_solo_la_version_vigente_de_cada_cita_con_el_medico_y_la_fecha()
    {
        ActAs(UserRole.Patient);

        var history = (await Handle(patientId: null)).Value;

        history.PatientId.ShouldBe(PatientId);
        history.Entries.Select(entry => entry.AppointmentId).ShouldBe([1, 2, 3]);
        var first = history.Entries[0];
        first.DoctorName.ShouldBe("Ana Pérez");
        first.SpecialtyName.ShouldBe("Medicina general");
        first.StartsAt.ShouldBe(StartsAt.AddDays(-1));
        first.CurrentVersion.ShouldBe(Dto(OwnCurrent));
        history.Entries.ShouldAllBe(entry => entry.PreviousVersions.Count == 0);
    }

    [Fact]
    public async Task Una_cita_completada_sin_registro_se_indica_sin_version()
    {
        ActAs(UserRole.Patient);

        var entry = (await Handle(patientId: null)).Value.Entries[1];

        entry.HasMedicalRecord.ShouldBeFalse();
        entry.CurrentVersion.ShouldBeNull();
        entry.PreviousVersions.ShouldBeEmpty();
    }

    [Fact]
    public async Task El_medico_tratante_ve_las_versiones_anteriores_solo_de_las_citas_que_atendio()
    {
        ActAs(UserRole.Doctor);

        var entries = (await Handle(PatientId)).Value.Entries;

        entries[0].CurrentVersion.ShouldBe(Dto(OwnCurrent));
        entries[0].PreviousVersions.ShouldBe([Dto(OwnPrevious)]);
        entries[2].CurrentVersion.ShouldBe(Dto(OtherCurrent));
        entries[2].PreviousVersions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cada_version_mostrada_al_paciente_queda_como_consulta_en_el_registro_de_accesos()
    {
        ActAs(UserRole.Patient);

        await Handle(patientId: null);

        AddedLogs().Select(log => log.MedicalRecordId).ShouldBe([OwnCurrent.Id, OtherCurrent.Id]);
        AddedLogs().ShouldAllBe(log => log.AccessType == MedicalRecordAccessType.View
                                       && log.AccessedBy == UserId
                                       && log.AccessedAt == _clock.UtcNow
                                       && log.IpAddress == IpAddress);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Al_medico_tratante_se_le_registran_tambien_las_versiones_anteriores_que_ve()
    {
        ActAs(UserRole.Doctor);

        await Handle(PatientId);

        AddedLogs().Select(log => log.MedicalRecordId).ShouldBe([OwnCurrent.Id, OwnPrevious.Id, OtherCurrent.Id]);
    }

    [Fact]
    public async Task Un_historial_sin_registros_no_deja_filas_ni_guarda()
    {
        ActAs(UserRole.Patient);
        _reader.ListAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns([Appointment(2, DoctorId, AppointmentStatus.Completed)]);

        (await Handle(patientId: null)).IsSuccess.ShouldBeTrue();

        _accessLog.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_medico_sin_citas_con_el_paciente_se_rechaza_sin_leer_ni_registrar_nada()
    {
        ActAs(UserRole.Doctor);
        _relationships.HasAppointmentWithPatientAsync(DoctorId, PatientId, Arg.Any<CancellationToken>()).Returns(false);

        AssertRejected(await Handle(PatientId), MedicalHistoryErrors.NoAppointmentWithPatient);
    }

    [Fact]
    public async Task Un_medico_sin_paciente_pedido_no_consulta_un_historial_propio()
    {
        ActAs(UserRole.Doctor);

        AssertRejected(await Handle(patientId: null), MedicalHistoryErrors.PatientOnly);
    }

    [Fact]
    public async Task Un_paciente_no_consulta_el_historial_de_otro_paciente()
    {
        ActAs(UserRole.Patient);

        AssertRejected(await Handle(PatientId), MedicalHistoryErrors.DoctorOnly);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Un_id_de_paciente_no_positivo_es_un_error_de_validacion(int patientId)
    {
        ActAs(UserRole.Doctor);

        var result = await Handle(patientId);

        result.Error.Code.ShouldBe("MedicalHistory.InvalidPatientId");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<MedicalHistoryDto>> Handle(int? patientId) =>
        _handler.HandleAsync(new GetMedicalHistoryQuery(patientId, IpAddress), CancellationToken.None);

    private void AssertRejected(Result<MedicalHistoryDto> result, Error error)
    {
        result.Error.ShouldBe(error);
        _reader.ReceivedCalls().ShouldBeEmpty();
        _accessLog.ReceivedCalls().ShouldBeEmpty();
    }

    private List<MedicalRecordAccessLog> AddedLogs() =>
        _accessLog.ReceivedCalls().Select(call => (MedicalRecordAccessLog)call.GetArguments()[0]!).ToList();

    private void ActAs(UserRole role) =>
        _currentUser.IsInRole(Arg.Any<UserRole>()).Returns(call => call.Arg<UserRole>() == role);

    private static MedicalHistoryAppointmentData Appointment(
        int id, int doctorId, AppointmentStatus status, params MedicalHistoryVersionData[] versions) =>
        new(id, StartsAt.AddDays(-id), StartsAt.AddDays(-id).AddHours(2), status, doctorId, "Ana Pérez",
            "Medicina general", versions);

    private static MedicalHistoryVersionData Version(int id, string diagnosis, DateTimeOffset createdAt) =>
        new(id, diagnosis, Treatment: null, Notes: null, createdAt);

    private static MedicalRecordVersionDto Dto(MedicalHistoryVersionData version) =>
        new(version.Id, version.Diagnosis, version.Treatment, version.Notes, version.CreatedAt);
}
