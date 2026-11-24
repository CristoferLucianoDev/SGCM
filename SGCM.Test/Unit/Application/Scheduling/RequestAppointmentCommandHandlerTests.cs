using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.RequestAppointment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class RequestAppointmentCommandHandlerTests
{
    private const int UserId = 42;
    private const int PatientId = 11;
    private const int SpecialtyId = 5;
    private const int ProvinceId = 3;
    private const int MunicipalityId = 4;
    private const int FirstDoctor = 7;
    private const int SecondDoctor = 8;
    private const int ThirdDoctor = 9;
    private const int AppointmentId = 99;

    // Hoy es lunes 4 de enero de 2027 a las 12:00 en Santo Domingo; la ventana del paciente empieza el 8 de
    // enero a las 00:00 locales (04:00 UTC). La cita es el martes 12 de 13:00 a 15:00 UTC.
    private static readonly DateTimeOffset WindowStart = new(2027, 1, 8, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndsAt = StartsAt.AddHours(2);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorAssignmentService _assignment = Substitute.For<IDoctorAssignmentService>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IActiveAppointmentReader _booked = Substitute.For<IActiveAppointmentReader>();
    private readonly IDoctorProfileReader _profiles = Substitute.For<IDoctorProfileReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 16, 0, 0, TimeSpan.Zero) };
    private readonly HashSet<int> _doctorsWithoutTheSlot = [];
    private Appointment? _added;

    public RequestAppointmentCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);

        _doctors.ListActiveDoctorIdsBySpecialtyAsync(
                Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<int>)new[] { FirstDoctor, SecondDoctor, ThirdDoctor });
        _blocks.LockOpenBlockAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _booked.ListActiveIntervalsAsync(
                Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => BookedFor(call.Arg<IReadOnlyCollection<int>>()));
        GivenAssignments(SecondDoctor);
        _profiles.GetPublicAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Profile(call.Arg<int>()));
        _appointments.When(repository => repository.Add(Arg.Any<Appointment>()))
            .Do(call => _added = call.Arg<Appointment>());
        _unitOfWork.When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(_added, AppointmentId));
    }

    [Fact]
    public async Task Con_medico_elegido_y_libre_crea_la_cita_pendiente_con_ese_medico()
    {
        var result = await Handle(Chosen(FirstDoctor) with { ReasonForVisit = "  Control anual  " });

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new RequestedAppointmentDto(AppointmentId, FirstDoctor, "Médico 7", 1700m, StartsAt, EndsAt, false));
        _added.ShouldNotBeNull();
        _added.PatientId.ShouldBe(PatientId);
        _added.DoctorId.ShouldBe(FirstDoctor);
        _added.SpecialtyId.ShouldBe(SpecialtyId);
        _added.Status.ShouldBe(AppointmentStatus.Pending);
        _added.ReasonForVisit.ShouldBe("Control anual");
        await _unitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
        _assignment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Con_medico_elegido_bloquea_su_bloque_y_vuelve_a_comprobar_sus_citas_en_el_horario()
    {
        await Handle(Chosen(FirstDoctor));

        Received.InOrder(() =>
        {
            _blocks.LockOpenBlockAsync(FirstDoctor, StartsAt, EndsAt, CancellationToken.None);
            _booked.ListActiveIntervalsAsync(
                Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { FirstDoctor })), StartsAt, EndsAt, CancellationToken.None);
            _appointments.Add(Arg.Any<Appointment>());
        });
        // El médico elegido se busca en la especialidad sin filtros de ubicación.
        await _doctors.Received(1).ListActiveDoctorIdsBySpecialtyAsync(SpecialtyId, null, null, CancellationToken.None);
    }

    [Fact]
    public async Task Si_el_medico_elegido_perdio_el_horario_se_rechaza_sin_asignar_otro()
    {
        _doctorsWithoutTheSlot.Add(FirstDoctor);

        var result = await Handle(Chosen(FirstDoctor));

        AssertRejected(result, AppointmentErrors.SlotUnavailable);
        _assignment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_medico_elegido_ya_no_tiene_un_bloque_abierto_que_contenga_el_horario_se_rechaza()
    {
        _blocks.LockOpenBlockAsync(FirstDoctor, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        AssertRejected(await Handle(Chosen(FirstDoctor)), AppointmentErrors.SlotUnavailable);
    }

    [Fact]
    public async Task Un_medico_elegido_que_no_atiende_la_especialidad_se_rechaza_sin_bloquear_nada()
    {
        var result = await Handle(Chosen(55));

        AssertRejected(result, AppointmentErrors.DoctorNotInSpecialty);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_medico_asignado_en_el_resumen_sigue_libre_se_conserva_sin_volver_a_asignar()
    {
        var result = await Handle(Assigned(ThirdDoctor));

        result.Value.DoctorId.ShouldBe(ThirdDoctor);
        result.Value.DoctorChanged.ShouldBeFalse();
        _assignment.ReceivedCalls().ShouldBeEmpty();
        await _doctors.Received(1).ListActiveDoctorIdsBySpecialtyAsync(SpecialtyId, ProvinceId, MunicipalityId, CancellationToken.None);
    }

    [Fact]
    public async Task Si_el_medico_asignado_perdio_el_horario_se_asigna_otro_libre_y_se_informa_el_cambio()
    {
        _doctorsWithoutTheSlot.Add(ThirdDoctor);

        var result = await Handle(Assigned(ThirdDoctor));

        result.IsSuccess.ShouldBeTrue();
        result.Value.DoctorId.ShouldBe(SecondDoctor);
        result.Value.DoctorName.ShouldBe("Médico 8");
        result.Value.ConsultationFee.ShouldBe(1800m);
        result.Value.DoctorChanged.ShouldBeTrue();
        _added!.DoctorId.ShouldBe(SecondDoctor);
        await _assignment.Received(1).AssignAsync(SpecialtyId, StartsAt, ProvinceId, MunicipalityId, CancellationToken.None);
    }

    [Fact]
    public async Task Si_el_reemplazo_tambien_pierde_el_horario_se_sigue_asignando_hasta_encontrar_uno_libre()
    {
        _doctorsWithoutTheSlot.UnionWith([ThirdDoctor, SecondDoctor]);
        GivenAssignments(SecondDoctor, FirstDoctor);

        var result = await Handle(Assigned(ThirdDoctor));

        result.Value.DoctorId.ShouldBe(FirstDoctor);
        result.Value.DoctorChanged.ShouldBeTrue();
        await _assignment.Received(2).AssignAsync(
            Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Solo_se_rechaza_por_horario_no_disponible_si_no_queda_ningun_medico_libre()
    {
        _doctorsWithoutTheSlot.Add(ThirdDoctor);
        _assignment.AssignAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(AppointmentErrors.SlotUnavailable));

        AssertRejected(await Handle(Assigned(ThirdDoctor)), AppointmentErrors.SlotUnavailable);
    }

    [Fact]
    public async Task Si_el_servicio_devuelve_otra_vez_un_medico_ya_intentado_se_rechaza_sin_repetir()
    {
        _doctorsWithoutTheSlot.UnionWith([ThirdDoctor, SecondDoctor]);
        GivenAssignments(SecondDoctor, SecondDoctor, FirstDoctor);

        AssertRejected(await Handle(Assigned(ThirdDoctor)), AppointmentErrors.SlotUnavailable);
        await _blocks.Received(1).LockOpenBlockAsync(SecondDoctor, StartsAt, EndsAt, CancellationToken.None);
    }

    [Fact]
    public async Task Un_medico_asignado_que_ya_no_es_de_la_especialidad_o_la_ubicacion_se_reemplaza()
    {
        var result = await Handle(Assigned(55));

        result.Value.DoctorId.ShouldBe(SecondDoctor);
        result.Value.DoctorChanged.ShouldBeTrue();
        await _blocks.DidNotReceive().LockOpenBlockAsync(55, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_medico_elegido_ni_asignado_el_sistema_asigna_uno_sin_marcar_cambio()
    {
        var result = await Handle(new RequestAppointmentCommand(SpecialtyId, StartsAt, ProvinceId: ProvinceId));

        result.Value.DoctorId.ShouldBe(SecondDoctor);
        result.Value.DoctorChanged.ShouldBeFalse();
        await _assignment.Received(1).AssignAsync(SpecialtyId, StartsAt, ProvinceId, null, CancellationToken.None);
    }

    [Fact]
    public async Task Un_horario_a_menos_de_la_antelacion_minima_pide_llamar_al_medico_sin_buscar_medicos()
    {
        var result = await Handle(Chosen(FirstDoctor) with { StartsAt = WindowStart.AddHours(-1) });

        AssertRejected(result, AppointmentErrors.TooSoon);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_inicio_que_no_esta_en_punto_se_rechaza_sin_buscar_medicos()
    {
        AssertRejected(await Handle(Assigned(ThirdDoctor) with { StartsAt = StartsAt.AddMinutes(30) }), AppointmentErrors.NotOnHour);
        _doctors.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_paciente_sin_perfil_completo_no_puede_agendar()
    {
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Patient?)null);

        AssertRejected(await Handle(Chosen(FirstDoctor)), PatientErrors.ProfileNotFound);
        _doctors.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Solo_un_paciente_puede_agendar()
    {
        _currentUser.IsInRole(UserRole.Patient).Returns(false);

        AssertRejected(await Handle(Chosen(FirstDoctor)), AppointmentErrors.PatientOnly);
        _patients.ReceivedCalls().ShouldBeEmpty();
    }

    private void AssertRejected(Result<RequestedAppointmentDto> result, Error error)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        _appointments.DidNotReceive().Add(Arg.Any<Appointment>());
        _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private void GivenAssignments(int first, params int[] rest) =>
        _assignment.AssignAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(first), rest.Select(id => Result<int>.Success(id)).ToArray());

    private IReadOnlyDictionary<int, IReadOnlyList<BookedInterval>> BookedFor(IReadOnlyCollection<int> doctorIds) =>
        doctorIds.Where(_doctorsWithoutTheSlot.Contains).ToDictionary(
            id => id,
            id => (IReadOnlyList<BookedInterval>)new[] { new BookedInterval(id * 10, TimeRange.Create(StartsAt.AddHours(1), EndsAt.AddHours(1)).Value) });

    private static DoctorPublicProfileDto Profile(int doctorId) => new(
        doctorId,
        $"Médico {doctorId}",
        [new DoctorPublicProfileDto.Reference(SpecialtyId, "Cardiología")],
        new DoctorPublicProfileDto.Reference(ProvinceId, "Santo Domingo"),
        new DoctorPublicProfileDto.Reference(MunicipalityId, "Santo Domingo Este"),
        "Calle de pruebas 226",
        1000m + doctorId * 100,
        10,
        "Perfil médico de prueba.");

    private static RequestAppointmentCommand Chosen(int doctorId) =>
        new(SpecialtyId, StartsAt, DoctorId: doctorId);

    private static RequestAppointmentCommand Assigned(int doctorId) =>
        new(SpecialtyId, StartsAt, AssignedDoctorId: doctorId, ProvinceId: ProvinceId, MunicipalityId: MunicipalityId);

    private Task<Result<RequestedAppointmentDto>> Handle(RequestAppointmentCommand command) =>
        CreateHandler().HandleAsync(command, CancellationToken.None);

    private RequestAppointmentCommandHandler CreateHandler() => new(
        _currentUser,
        _patients,
        _doctors,
        _assignment,
        _blocks,
        _booked,
        _profiles,
        _appointments,
        _unitOfWork,
        _clock,
        BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone),
        Options.Create(new SchedulingOptions
        {
            TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
            AppointmentDurationHours = 2,
            MinimumBlockDurationHours = 6,
            MinimumBookingLeadDays = 4,
            BookingHorizonDays = 90
        }));
}
