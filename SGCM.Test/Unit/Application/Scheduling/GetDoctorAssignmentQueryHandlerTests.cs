using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetDoctorAssignmentQueryHandlerTests
{
    private const int SpecialtyId = 5;
    private const int ProvinceId = 3;
    private const int MunicipalityId = 4;
    private const int DoctorId = 7;

    // Hoy es lunes 4 de enero de 2027 a las 12:00 en Santo Domingo: la ventana del paciente va del 8 de enero
    // al 5 de abril a las 00:00 locales (04:00 UTC).
    private static readonly DateTimeOffset WindowStart = new(2027, 1, 8, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2027, 4, 5, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 13, 0, 0, TimeSpan.Zero);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDoctorAssignmentService _assignment = Substitute.For<IDoctorAssignmentService>();
    private readonly IDoctorProfileReader _profiles = Substitute.For<IDoctorProfileReader>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 16, 0, 0, TimeSpan.Zero) };

    public GetDoctorAssignmentQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
        _assignment.AssignAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(DoctorId));
        _profiles.GetPublicAsync(DoctorId, Arg.Any<CancellationToken>()).Returns(Profile());
    }

    [Fact]
    public async Task Devuelve_el_medico_asignado_con_su_nombre_y_su_tarifa()
    {
        var result = await Handle(Query());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new DoctorAssignmentDto(DoctorId, "Luis Pérez", 1500m));
    }

    [Fact]
    public async Task Pasa_la_especialidad_el_horario_y_los_filtros_al_servicio_de_asignacion()
    {
        using var cancellation = new CancellationTokenSource();

        await CreateHandler().HandleAsync(Query(), cancellation.Token);

        await _assignment.Received(1).AssignAsync(SpecialtyId, StartsAt, ProvinceId, MunicipalityId, cancellation.Token);
        await _profiles.Received(1).GetPublicAsync(DoctorId, cancellation.Token);
    }

    [Fact]
    public async Task Sin_filtros_de_ubicacion_los_pasa_nulos()
    {
        await Handle(new GetDoctorAssignmentQuery(SpecialtyId, StartsAt));

        await _assignment.Received(1).AssignAsync(SpecialtyId, StartsAt, null, null, CancellationToken.None);
    }

    [Fact]
    public async Task El_primer_instante_de_la_ventana_se_acepta()
    {
        (await Handle(Query(WindowStart))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_horario_antes_de_la_antelacion_minima_pide_llamar_al_medico_sin_asignar()
    {
        var result = await Handle(Query(WindowStart.AddHours(-1)));

        result.Error.ShouldBe(AppointmentErrors.TooSoon);
        _assignment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_horario_desde_el_fin_del_horizonte_se_rechaza_sin_asignar()
    {
        var result = await Handle(Query(WindowEnd));

        result.Error.ShouldBe(AppointmentErrors.BeyondHorizon);
        _assignment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_ningun_medico_tiene_libre_el_horario_devuelve_el_error_del_servicio()
    {
        _assignment.AssignAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(AppointmentErrors.SlotUnavailable));

        var result = await Handle(Query());

        result.Error.ShouldBe(AppointmentErrors.SlotUnavailable);
        _profiles.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_perfil_del_medico_ya_no_esta_disponible_es_horario_no_disponible()
    {
        _profiles.GetPublicAsync(DoctorId, Arg.Any<CancellationToken>()).Returns((DoctorPublicProfileDto?)null);

        (await Handle(Query())).Error.ShouldBe(AppointmentErrors.SlotUnavailable);
    }

    [Fact]
    public async Task Un_medico_no_puede_pedir_la_asignacion()
    {
        _currentUser.IsInRole(UserRole.Patient).Returns(false);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);

        AssertPatientOnly(await Handle(Query()));
    }

    [Fact]
    public async Task Sin_autenticar_no_se_puede_pedir_la_asignacion()
    {
        _currentUser.IsAuthenticated.Returns(false);

        AssertPatientOnly(await Handle(Query()));
    }

    [Theory]
    [InlineData(0, null, null, "DoctorAssignment.InvalidSpecialtyId")]
    [InlineData(-1, null, null, "DoctorAssignment.InvalidSpecialtyId")]
    [InlineData(SpecialtyId, 0, null, "DoctorAssignment.InvalidProvinceId")]
    [InlineData(SpecialtyId, null, 0, "DoctorAssignment.InvalidMunicipalityId")]
    public async Task Los_identificadores_invalidos_se_rechazan_antes_de_todo(
        int specialtyId, int? provinceId, int? municipalityId, string code)
    {
        var result = await Handle(new GetDoctorAssignmentQuery(specialtyId, StartsAt, provinceId, municipalityId));

        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _assignment.ReceivedCalls().ShouldBeEmpty();
    }

    private void AssertPatientOnly(Result<DoctorAssignmentDto> result)
    {
        result.Error.ShouldBe(AppointmentErrors.PatientOnly);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        _assignment.ReceivedCalls().ShouldBeEmpty();
    }

    private static GetDoctorAssignmentQuery Query(DateTimeOffset? startsAt = null) =>
        new(SpecialtyId, startsAt ?? StartsAt, ProvinceId, MunicipalityId);

    private static DoctorPublicProfileDto Profile() => new(
        DoctorId,
        "Luis Pérez",
        [new DoctorPublicProfileDto.Reference(SpecialtyId, "Cardiología")],
        new DoctorPublicProfileDto.Reference(ProvinceId, "Santo Domingo"),
        new DoctorPublicProfileDto.Reference(MunicipalityId, "Santo Domingo Este"),
        "Calle de pruebas 226",
        1500m,
        10,
        "Perfil médico de prueba.");

    private Task<Result<DoctorAssignmentDto>> Handle(GetDoctorAssignmentQuery query) =>
        CreateHandler().HandleAsync(query, CancellationToken.None);

    private GetDoctorAssignmentQueryHandler CreateHandler() => new(
        _currentUser,
        _assignment,
        _profiles,
        _clock,
        BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone),
        Options.Create(new SchedulingOptions
        {
            TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
            AppointmentDurationHours = 2,
            MinimumBlockDurationHours = 6,
            MinimumBookingLeadDays = 4,
            BookingHorizonDays = 90
        }),
        new GetDoctorAssignmentQueryValidator());
}
