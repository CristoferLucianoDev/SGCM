using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class DoctorAssignmentServiceTests
{
    private const int SpecialtyId = 5;
    private const int ProvinceId = 3;
    private const int MunicipalityId = 4;
    private const int FirstDoctor = 7;
    private const int SecondDoctor = 8;
    private const int ThirdDoctor = 9;
    private const int AppointmentHours = 2;
    private const int MinimumBlockHours = 6;

    // Martes 12 de enero de 2027: los bloques son de 12:00 a 18:00 UTC y la cita empieza a las 14:00 UTC.
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartsAt = BlockStart.AddHours(2);

    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IActiveAppointmentReader _appointments = Substitute.For<IActiveAppointmentReader>();
    private readonly IRandomNumberGenerator _random = Substitute.For<IRandomNumberGenerator>();

    public DoctorAssignmentServiceTests()
    {
        GivenCandidates(FirstDoctor, SecondDoctor, ThirdDoctor);
        GivenBlocks(Block(FirstDoctor), Block(SecondDoctor), Block(ThirdDoctor));
        GivenAppointments(new Dictionary<int, IReadOnlyList<BookedInterval>>());
        _random.Next(Arg.Any<int>()).Returns(0);
    }

    [Fact]
    public async Task Elige_con_el_azar_inyectado_entre_los_medicos_que_tienen_libre_el_horario()
    {
        GivenAppointments(new Dictionary<int, IReadOnlyList<BookedInterval>>
        {
            [SecondDoctor] = [Booked(StartsAt.AddHours(1), StartsAt.AddHours(3))]
        });
        _random.Next(2).Returns(1);

        var result = await Assign();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ThirdDoctor);
        _random.Received(1).Next(2);
    }

    [Theory]
    [InlineData(0, FirstDoctor)]
    [InlineData(1, SecondDoctor)]
    [InlineData(2, ThirdDoctor)]
    public async Task El_indice_del_azar_se_aplica_a_los_libres_en_el_orden_del_lector(int index, int expected)
    {
        _random.Next(3).Returns(index);

        (await Assign()).Value.ShouldBe(expected);
    }

    [Fact]
    public async Task Busca_a_los_medicos_activos_de_la_especialidad_con_los_filtros_de_ubicacion()
    {
        await Assign();

        await _doctors.Received(1).ListActiveDoctorIdsBySpecialtyAsync(
            SpecialtyId, ProvinceId, MunicipalityId, CancellationToken.None);
    }

    [Fact]
    public async Task Lee_bloques_y_citas_una_sola_vez_para_todos_los_candidatos_en_el_rango_de_la_cita()
    {
        await Assign();

        var endsAt = StartsAt.AddHours(AppointmentHours);
        await _blocks.Received(1).ListByDoctorsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { FirstDoctor, SecondDoctor, ThirdDoctor })),
            StartsAt, endsAt, CancellationToken.None);
        await _appointments.Received(1).ListActiveIntervalsAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { FirstDoctor, SecondDoctor, ThirdDoctor })),
            StartsAt, endsAt, CancellationToken.None);
    }

    [Fact]
    public async Task Un_medico_cuyo_bloque_termina_antes_que_la_cita_no_tiene_libre_el_horario()
    {
        // La cita de 17:00 a 19:00 UTC no cabe en el bloque de 12:00 a 18:00.
        GivenCandidates(FirstDoctor);
        GivenBlocks(Block(FirstDoctor));

        var result = await Assign(BlockStart.AddHours(5));

        result.Error.ShouldBe(AppointmentErrors.SlotUnavailable);
    }

    [Fact]
    public async Task Un_medico_con_el_bloque_cerrado_no_tiene_libre_el_horario()
    {
        var closed = Block(FirstDoctor);
        // El dominio todavía no expone cómo cerrar un bloque; la base lo guarda con is_available en false.
        typeof(DoctorAvailability).GetProperty(nameof(DoctorAvailability.IsAvailable))!.SetValue(closed, false);
        GivenBlocks(closed, Block(SecondDoctor));

        (await Assign()).Value.ShouldBe(SecondDoctor);
        _random.Received(1).Next(1);
    }

    [Fact]
    public async Task Una_cita_contigua_no_quita_el_horario()
    {
        GivenCandidates(FirstDoctor);
        GivenBlocks(Block(FirstDoctor));
        GivenAppointments(new Dictionary<int, IReadOnlyList<BookedInterval>>
        {
            [FirstDoctor] = [Booked(BlockStart, StartsAt), Booked(StartsAt.AddHours(AppointmentHours), BlockStart.AddHours(6))]
        });

        (await Assign()).Value.ShouldBe(FirstDoctor);
    }

    [Fact]
    public async Task Si_ningun_medico_tiene_libre_el_horario_es_horario_no_disponible_sin_usar_el_azar()
    {
        GivenBlocks();

        var result = await Assign();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AppointmentErrors.SlotUnavailable);
        _random.DidNotReceive().Next(Arg.Any<int>());
    }

    [Fact]
    public async Task Sin_medicos_en_la_especialidad_y_ubicacion_es_horario_no_disponible_sin_leer_la_agenda()
    {
        GivenCandidates();

        var result = await Assign();

        result.Error.ShouldBe(AppointmentErrors.SlotUnavailable);
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
        _random.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_inicio_que_no_esta_en_punto_se_rechaza_sin_consultar_nada()
    {
        var result = await Assign(StartsAt.AddMinutes(30));

        result.Error.ShouldBe(AppointmentErrors.NotOnHour);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_inicio_con_otro_desfase_se_compara_por_instante()
    {
        _random.Next(3).Returns(2);

        var result = await Assign(StartsAt.ToOffset(TimeSpan.FromHours(-4)));

        result.Value.ShouldBe(ThirdDoctor);
        await _blocks.Received(1).ListByDoctorsAsync(
            Arg.Any<IReadOnlyCollection<int>>(), StartsAt, StartsAt.AddHours(AppointmentHours), CancellationToken.None);
    }

    private Task<Result<int>> Assign(DateTimeOffset? startsAt = null) =>
        CreateService().AssignAsync(SpecialtyId, startsAt ?? StartsAt, ProvinceId, MunicipalityId, CancellationToken.None);

    private DoctorAssignmentService CreateService() => new(
        _doctors,
        _blocks,
        _appointments,
        _random,
        Options.Create(new SchedulingOptions { AppointmentDurationHours = AppointmentHours }));

    private void GivenCandidates(params int[] doctorIds) =>
        _doctors.ListActiveDoctorIdsBySpecialtyAsync(
                Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<int>)doctorIds);

    private void GivenBlocks(params DoctorAvailability[] blocks) =>
        _blocks.ListByDoctorsAsync(
                Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DoctorAvailability>)blocks);

    private void GivenAppointments(IReadOnlyDictionary<int, IReadOnlyList<BookedInterval>> byDoctor) =>
        _appointments.ListActiveIntervalsAsync(
                Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(byDoctor);

    private static DoctorAvailability Block(int doctorId) =>
        DoctorAvailability.Create(doctorId, null, BlockStart, BlockStart.AddHours(MinimumBlockHours), MinimumBlockHours).Value;

    private static BookedInterval Booked(DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        new(1, TimeRange.Create(startsAt, endsAt).Value);
}
