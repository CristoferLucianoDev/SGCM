using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Clinical;
using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Clinical;

/// <summary>
/// Pruebas de <see cref="CreateMedicalRecordVersionCommandHandler"/>: el médico de una cita confirmada o completada
/// registra desde su hora de inicio; la primera vez crea el registro y después corrige con una versión nueva.
/// </summary>
public sealed class CreateMedicalRecordVersionCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int SavedId = 501;
    private const int OtherDoctorId = AppointmentMother.DoctorId + 1;
    private const string IpAddress = "10.0.0.5";

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IMedicalRecordRepository _records = Substitute.For<IMedicalRecordRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly MedicalRecordAccessTracker _tracker = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly List<MedicalRecord> _added = [];
    private readonly CreateMedicalRecordVersionCommandHandler _handler;

    private Appointment _appointment = AppointmentWithId(AppointmentStatus.Confirmed);

    public CreateMedicalRecordVersionCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_ => _appointment);
        _records.When(records => records.Add(Arg.Any<MedicalRecord>())).Do(call => _added.Add(call.Arg<MedicalRecord>()));
        // Simula la base: al guardar, la versión nueva recibe su id.
        _unitOfWork.When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => _added.Where(record => record.Id == 0).ToList().ForEach(record => AssignId(record, SavedId)));
        _clock.UtcNow = AppointmentMother.StartsAt.AddMinutes(30);

        var access = new DoctorAppointmentAccess(_currentUser, _doctors, _appointments);
        _handler = new CreateMedicalRecordVersionCommandHandler(
            access, _records, _tracker, _currentUser, _unitOfWork, _clock);
    }

    [Fact]
    public async Task La_primera_vez_crea_el_registro_de_la_cita_y_lo_informa_como_creacion()
    {
        var result = await Handle("  Faringitis ", "Reposo", null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new MedicalRecordVersionResult(
            SavedId, AppointmentId, "Faringitis", "Reposo", null, _clock.UtcNow, IsCorrection: false));
        var record = _added.ShouldHaveSingleItem();
        record.AppointmentId.ShouldBe(AppointmentId);
        record.PatientId.ShouldBe(AppointmentMother.PatientId);
        record.CreatedBy.ShouldBe(UserId);
        _tracker.Accesses.ShouldBe([new MedicalRecordAccess(record, MedicalRecordAccessType.Create)]);
    }

    [Fact]
    public async Task Con_registro_previo_crea_una_version_nueva_sin_tocar_la_vigente()
    {
        var current = MedicalRecord.Create(
            AppointmentId, AppointmentMother.PatientId, MedicalRecordContent.Create("Faringitis", null, null).Value,
            UserId, AppointmentMother.StartsAt.AddMinutes(10));
        AssignId(current, SavedId - 1);
        _records.GetCurrentAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(current);

        var result = await Handle("Amigdalitis", null, "Control en 7 días");

        result.Value.ShouldBe(new MedicalRecordVersionResult(
            SavedId, AppointmentId, "Amigdalitis", null, "Control en 7 días", _clock.UtcNow, IsCorrection: true));
        current.Diagnosis.ShouldBe("Faringitis");
        current.Notes.ShouldBeNull();
        var correction = _added.ShouldHaveSingleItem();
        correction.ShouldNotBeSameAs(current);
        _tracker.Accesses.ShouldBe([new MedicalRecordAccess(correction, MedicalRecordAccessType.Update)]);
    }

    [Fact]
    public async Task Guarda_despues_de_agregar_la_version_para_devolver_su_id()
    {
        await Handle("Faringitis", null, null);

        Received.InOrder(() =>
        {
            _records.Add(Arg.Any<MedicalRecord>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Una_cita_completada_tambien_admite_registro()
    {
        _appointment = AppointmentWithId(AppointmentStatus.Completed);
        _clock.UtcNow = _appointment.EndsAt.AddHours(1);

        (await Handle("Faringitis", null, null)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Rejected)]
    public async Task Una_cita_que_no_esta_confirmada_ni_completada_se_rechaza(AppointmentStatus status)
    {
        _appointment = AppointmentWithId(status);

        AssertRejected(await Handle("Faringitis", null, null), AppointmentErrors.ClinicalRecordInvalidStatus);
    }

    [Fact]
    public async Task Una_cita_que_aun_no_empieza_se_rechaza()
    {
        _clock.UtcNow = AppointmentMother.StartsAt.AddSeconds(-1);

        AssertRejected(await Handle("Faringitis", null, null), AppointmentErrors.ClinicalRecordNotStarted);
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_como_inexistente()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)OtherDoctorId);

        AssertRejected(await Handle("Faringitis", null, null), AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_inexistente_se_rechaza_como_no_encontrada()
    {
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        AssertRejected(await Handle("Faringitis", null, null), AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_se_rechaza()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        AssertRejected(await Handle("Faringitis", null, null), DoctorErrors.NotFound);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Quien_no_es_medico_se_rechaza_sin_consultar_nada(bool authenticated, bool isDoctor)
    {
        _currentUser.IsAuthenticated.Returns(authenticated);
        _currentUser.IsInRole(UserRole.Doctor).Returns(isDoctor);

        AssertRejected(await Handle("Faringitis", null, null), AppointmentErrors.ClinicalRecordDoctorOnly);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_contenido_invalido_se_rechaza_con_el_error_del_dominio()
    {
        AssertRejected(await Handle(" ", null, ""), MedicalRecordErrors.Empty);
    }

    private Task<Result<MedicalRecordVersionResult>> Handle(string? diagnosis, string? treatment, string? notes) =>
        _handler.HandleAsync(
            new CreateMedicalRecordVersionCommand(AppointmentId, diagnosis, treatment, notes, IpAddress),
            CancellationToken.None);

    private void AssertRejected(Result<MedicalRecordVersionResult> result, Error error)
    {
        result.Error.ShouldBe(error);
        _records.DidNotReceive().Add(Arg.Any<MedicalRecord>());
        _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _tracker.Accesses.ShouldBeEmpty();
    }

    private static Appointment AppointmentWithId(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        AssignId(appointment, AppointmentId);
        return appointment;
    }

    // Entity.Id solo lo asigna la base; aquí se imita lo que hace EF al cargar o guardar.
    private static void AssignId(object entity, int id) =>
        entity.GetType().GetProperty("Id")!.SetValue(entity, id);
}
