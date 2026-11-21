using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetAvailableSlotsQueryHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int OtherDoctorId = 8;
    private const int AppointmentHours = 2;
    private const int MinimumBlockHours = 6;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IActiveAppointmentReader _appointments = Substitute.For<IActiveAppointmentReader>();

    // Lunes 4 de enero de 2027 a las 12:00 en Santo Domingo (UTC-4).
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 16, 0, 0, TimeSpan.Zero) };

    private readonly SchedulingOptions _options = new()
    {
        TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
        AppointmentDurationHours = AppointmentHours,
        MinimumBlockDurationHours = MinimumBlockHours,
        MinimumBookingLeadDays = 4,
        BookingHorizonDays = 90
    };

    // Ventana del paciente con hoy = 4 de enero: del 8 de enero al 5 de abril a las 00:00 locales.
    private static readonly DateTimeOffset PatientWindowStart = new(2027, 1, 8, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PatientWindowEnd = new(2027, 4, 5, 4, 0, 0, TimeSpan.Zero);

    // Domingo 10 de enero de 2027, de 8:00 a 16:00 UTC: dentro de la ventana del paciente.
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BlockEnd = new(2027, 1, 10, 16, 0, 0, TimeSpan.Zero);

    public GetAvailableSlotsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdAsync(DoctorId, Arg.Any<CancellationToken>()).Returns(DoctorId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(DoctorId);
        GivenBlocks();
        GivenAppointments();
    }

    [Fact]
    public async Task Un_paciente_con_DoctorId_usa_la_ventana_del_paciente_y_ofrece_los_horarios_libres()
    {
        AsPatient();
        GivenBlocks(Block(BlockStart, BlockEnd));
        GivenAppointments(Appointment(1, BlockStart.AddHours(2), BlockStart.AddHours(4)));

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(slot => slot.StartsAt.Hour).ShouldBe(new[] { 8, 12, 13, 14 });
        result.Value.ShouldAllBe(slot => slot.EndsAt - slot.StartsAt == TimeSpan.FromHours(AppointmentHours));
        result.Value.ShouldAllBe(slot => slot.StartsAt.Offset == TimeSpan.Zero && slot.EndsAt.Offset == TimeSpan.Zero);
        await _doctors.Received(1).FindActiveDoctorIdAsync(DoctorId, CancellationToken.None);
        await _doctors.DidNotReceive().FindActiveDoctorIdByUserIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _blocks.Received(1).ListByDoctorAsync(DoctorId, PatientWindowStart, PatientWindowEnd, CancellationToken.None);
        await _appointments.Received(1).ListActiveIntervalsAsync(
            DoctorId, PatientWindowStart, PatientWindowEnd, CancellationToken.None);
    }

    [Fact]
    public async Task Cada_horario_termina_la_duracion_de_la_cita_despues_de_su_inicio()
    {
        AsPatient();
        GivenBlocks(Block(BlockStart, BlockEnd));

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        result.Value.Select(slot => (slot.StartsAt.Hour, slot.EndsAt.Hour))
            .ShouldBe(new[] { (8, 10), (9, 11), (10, 12), (11, 13), (12, 14), (13, 15), (14, 16) });
    }

    [Fact]
    public async Task Un_bloque_cerrado_no_ofrece_horarios()
    {
        AsPatient();
        var closed = Block(BlockStart, BlockEnd);
        // El dominio todavía no expone cómo cerrar un bloque; la base lo guarda con is_available en false.
        typeof(DoctorAvailability).GetProperty(nameof(DoctorAvailability.IsAvailable))!.SetValue(closed, false);
        GivenBlocks(closed);

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_resuelve_su_perfil_por_su_usuario_y_usa_la_ventana_del_medico()
    {
        AsDoctor();
        // Domingo 10 de enero a las 09:30 UTC: la hora con minutos redondea al siguiente horario en punto.
        var now = new DateTimeOffset(2027, 1, 10, 9, 30, 0, TimeSpan.Zero);
        _clock.UtcNow = now;
        GivenBlocks(Block(BlockStart, BlockEnd));

        var result = await Handle(new GetAvailableSlotsQuery());

        result.Value.Select(slot => slot.StartsAt.Hour).ShouldBe(new[] { 10, 11, 12, 13, 14 });
        await _doctors.Received(1).FindActiveDoctorIdByUserIdAsync(UserId, CancellationToken.None);
        await _doctors.DidNotReceive().FindActiveDoctorIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        // 10 de enero + 90 días = 10 de abril; el fin es el 11 de abril a las 00:00 locales (04:00 UTC).
        var windowEnd = new DateTimeOffset(2027, 4, 11, 4, 0, 0, TimeSpan.Zero);
        await _blocks.Received(1).ListByDoctorAsync(DoctorId, now, windowEnd, CancellationToken.None);
        await _appointments.Received(1).ListActiveIntervalsAsync(DoctorId, now, windowEnd, CancellationToken.None);
    }

    [Fact]
    public async Task Un_medico_que_envia_su_propio_DoctorId_consulta_sus_horarios()
    {
        AsDoctor();
        _clock.UtcNow = new DateTimeOffset(2027, 1, 10, 9, 0, 0, TimeSpan.Zero);
        GivenBlocks(Block(BlockStart, BlockEnd));

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(slot => slot.StartsAt.Hour).ShouldBe(new[] { 9, 10, 11, 12, 13, 14 });
    }

    [Fact]
    public async Task Un_medico_que_envia_el_DoctorId_de_otro_recibe_OtherDoctorSchedule_sin_consultar_horarios()
    {
        AsDoctor();

        var result = await Handle(new GetAvailableSlotsQuery(OtherDoctorId));

        result.Error.ShouldBe(AvailableSlotsErrors.OtherDoctorSchedule);
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_paciente_sin_DoctorId_recibe_DoctorRequired_sin_tocar_los_lectores()
    {
        AsPatient();

        var result = await Handle(new GetAvailableSlotsQuery());

        result.Error.ShouldBe(AvailableSlotsErrors.DoctorRequired);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_paciente_con_un_medico_inexistente_o_inactivo_recibe_NotFound()
    {
        AsPatient();
        _doctors.FindActiveDoctorIdAsync(OtherDoctorId, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await Handle(new GetAvailableSlotsQuery(OtherDoctorId));

        result.Error.ShouldBe(DoctorErrors.NotFound);
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_recibe_NotFound()
    {
        AsDoctor();
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        result.Error.ShouldBe(DoctorErrors.NotFound);
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_administrador_recibe_RoleNotAllowed_sin_tocar_los_lectores()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Administrator).Returns(true);

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        AssertRoleNotAllowed(result);
    }

    [Fact]
    public async Task Un_usuario_anonimo_recibe_RoleNotAllowed_sin_tocar_los_lectores()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        AssertRoleNotAllowed(result);
    }

    [Fact]
    public async Task Un_usuario_sin_autenticar_con_rol_de_medico_recibe_RoleNotAllowed()
    {
        _currentUser.IsAuthenticated.Returns(false);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        AssertRoleNotAllowed(result);
    }

    [Fact]
    public async Task Sin_bloques_devuelve_exito_con_una_lista_vacia()
    {
        AsPatient();

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Con_todo_ocupado_devuelve_exito_con_una_lista_vacia()
    {
        AsPatient();
        GivenBlocks(Block(BlockStart, BlockEnd));
        GivenAppointments(Appointment(1, BlockStart, BlockEnd));

        var result = await Handle(new GetAvailableSlotsQuery(DoctorId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Un_DoctorId_invalido_se_rechaza_antes_de_consultar(int doctorId)
    {
        AsPatient();

        var result = await Handle(new GetAvailableSlotsQuery(doctorId));

        result.Error.Code.ShouldBe("AvailableSlots.InvalidDoctorId");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_token_cancelado_no_consulta_y_propaga_la_cancelacion()
    {
        AsPatient();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateHandler().HandleAsync(new GetAvailableSlotsQuery(DoctorId), source.Token));

        _doctors.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task La_cancelacion_de_un_lector_se_propaga()
    {
        AsPatient();
        GivenBlocks(Block(BlockStart, BlockEnd));
        _appointments.ListActiveIntervalsAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<BookedInterval>>(new OperationCanceledException()));

        await Should.ThrowAsync<OperationCanceledException>(() => Handle(new GetAvailableSlotsQuery(DoctorId)));
    }

    private void AsPatient()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
    }

    private void AsDoctor()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
    }

    private void GivenBlocks(params DoctorAvailability[] blocks) =>
        _blocks.ListByDoctorAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DoctorAvailability>)blocks);

    private void GivenAppointments(params BookedInterval[] appointments) =>
        _appointments.ListActiveIntervalsAsync(
                Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<BookedInterval>)appointments);

    private void AssertRoleNotAllowed(Result<IReadOnlyList<AvailableSlotDto>> result)
    {
        result.Error.ShouldBe(AvailableSlotsErrors.RoleNotAllowed);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    private static DoctorAvailability Block(DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        DoctorAvailability.Create(DoctorId, null, startsAt, endsAt, MinimumBlockHours).Value;

    private static BookedInterval Appointment(int id, DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        new(id, TimeRange.Create(startsAt, endsAt).Value);

    private Task<Result<IReadOnlyList<AvailableSlotDto>>> Handle(GetAvailableSlotsQuery query) =>
        CreateHandler().HandleAsync(query, CancellationToken.None);

    private GetAvailableSlotsQueryHandler CreateHandler() => new(
        _currentUser,
        _doctors,
        _blocks,
        _appointments,
        _clock,
        BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone),
        Options.Create(_options),
        new GetAvailableSlotsQueryValidator());
}
