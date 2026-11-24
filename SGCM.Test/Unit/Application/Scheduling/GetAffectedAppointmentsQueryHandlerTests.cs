using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetAffectedAppointmentsQueryHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int BlockId = 40;
    private static readonly DateTimeOffset BlockStart = new(2027, 1, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IAffectedAppointmentsPolicy _policy = Substitute.For<IAffectedAppointmentsPolicy>();
    private IReadOnlyCollection<TimeRange>? _evaluated;

    public GetAffectedAppointmentsQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(DoctorId);
        _blocks.GetByIdAsync(BlockId, Arg.Any<CancellationToken>()).Returns(Block(DoctorId));
        _policy.EvaluateAsync(DoctorId, Arg.Do<IReadOnlyCollection<TimeRange>>(ranges => _evaluated = ranges), Arg.Any<CancellationToken>())
            .Returns(new AffectedAppointments(
                [new BookedInterval(1, Range(0, 2)), new BookedInterval(2, Range(2, 4))], IsShortNotice: true,
                new ShortNoticeCancellation(6, 30)));
    }

    [Fact]
    public async Task Al_cerrar_evalua_el_bloque_entero_y_devuelve_el_aviso()
    {
        var result = await Handle(new GetAffectedAppointmentsQuery(BlockId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new AffectedAppointmentsDto(2, true, true, 6, 30));
        _evaluated.ShouldBe(new[] { Range(0, 6) });
    }

    [Fact]
    public async Task Al_reprogramar_evalua_solo_lo_que_queda_fuera_del_horario_nuevo()
    {
        await Handle(new GetAffectedAppointmentsQuery(BlockId, BlockStart.AddHours(2), BlockStart.AddHours(4)));

        _evaluated.ShouldBe(new[] { Range(0, 2), Range(4, 6) });
    }

    [Fact]
    public async Task Al_reprogramar_a_otro_dia_evalua_el_bloque_entero()
    {
        await Handle(new GetAffectedAppointmentsQuery(BlockId, BlockStart.AddDays(1), BlockStart.AddDays(1).AddHours(6)));

        _evaluated.ShouldBe(new[] { Range(0, 6) });
    }

    [Fact]
    public async Task Un_horario_nuevo_con_el_fin_antes_del_inicio_se_rechaza()
    {
        var result = await Handle(new GetAffectedAppointmentsQuery(BlockId, BlockStart.AddHours(4), BlockStart.AddHours(2)));

        result.Error.ShouldBe(DoctorAvailabilityErrors.InvalidTimeRange);
        _policy.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_bloque_de_otro_medico_se_trata_como_inexistente()
    {
        _blocks.GetByIdAsync(BlockId, Arg.Any<CancellationToken>()).Returns(Block(99));

        (await Handle(new GetAffectedAppointmentsQuery(BlockId))).Error.ShouldBe(DoctorAvailabilityErrors.NotFound);
        _policy.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_bloque_que_no_existe_responde_no_encontrado()
    {
        (await Handle(new GetAffectedAppointmentsQuery(55))).Error.ShouldBe(DoctorAvailabilityErrors.NotFound);
    }

    [Fact]
    public async Task Sin_perfil_de_medico_activo_responde_medico_no_encontrado()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        (await Handle(new GetAffectedAppointmentsQuery(BlockId))).Error.ShouldBe(DoctorErrors.NotFound);
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, false, false, "Availability.InvalidBlockId")]
    [InlineData(BlockId, true, false, "Availability.IncompleteNewSchedule")]
    [InlineData(BlockId, false, true, "Availability.IncompleteNewSchedule")]
    public async Task Las_consultas_invalidas_se_rechazan_antes_de_todo(int blockId, bool withStart, bool withEnd, string code)
    {
        var query = new GetAffectedAppointmentsQuery(
            blockId, withStart ? BlockStart : null, withEnd ? BlockStart.AddHours(2) : null);

        var result = await Handle(query);

        result.Error.Code.ShouldBe(code);
        _doctors.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<AffectedAppointmentsDto>> Handle(GetAffectedAppointmentsQuery query) =>
        new GetAffectedAppointmentsQueryHandler(_currentUser, _doctors, _blocks, _policy, new GetAffectedAppointmentsQueryValidator())
            .HandleAsync(query, CancellationToken.None);

    private static DoctorAvailability Block(int doctorId)
    {
        var block = DoctorAvailability.Create(doctorId, null, BlockStart, BlockStart.AddHours(6), 6).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(block, BlockId);
        return block;
    }

    private static TimeRange Range(int fromHour, int toHour) =>
        TimeRange.Create(BlockStart.AddHours(fromHour), BlockStart.AddHours(toHour)).Value;
}
