using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling.Queries.GetAvailabilityBlocks;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetAvailabilityBlocksQueryTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly DateOnly January1 = new(2027, 1, 1);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly GetAvailabilityBlocksQueryValidator _validator = new();
    // Lunes 28 de diciembre de 2026 a las 10:00 en Santo Domingo: la semana actual termina el domingo 3 de enero.
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 12, 28, 10, 0, 0, TimeSpan.FromHours(-4)) };

    public GetAvailabilityBlocksQueryTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(DoctorId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void Un_rango_de_hasta_31_dias_es_valido(int extraDays)
    {
        _validator.Validate(new GetAvailabilityBlocksQuery(January1, January1.AddDays(extraDays))).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_rango_de_32_dias_se_rechaza()
    {
        _validator.Validate(new GetAvailabilityBlocksQuery(January1, January1.AddDays(31))).Errors
            .ShouldHaveSingleItem().ErrorCode.ShouldBe("Availability.DateRangeTooLong");
    }

    [Fact]
    public void La_fecha_hasta_no_puede_ser_anterior_a_la_fecha_desde()
    {
        _validator.Validate(new GetAvailabilityBlocksQuery(January1, January1.AddDays(-1))).Errors
            .ShouldHaveSingleItem().ErrorCode.ShouldBe("Availability.InvalidDateRange");
    }

    [Fact]
    public async Task Devuelve_los_bloques_que_empiezan_entre_las_fechas_locales_abiertos_y_cerrados()
    {
        var before = Block(1, null, Local(2026, 12, 31, 20));
        var first = Block(2, 15, Local(2027, 1, 1, 20));
        var closed = Block(3, null, Local(2027, 1, 10, 8));
        closed.Close(null, Local(2027, 1, 1, 0)).IsSuccess.ShouldBeTrue();
        var last = Block(4, 15, Local(2027, 1, 31, 18));
        var after = Block(5, 15, Local(2027, 2, 1, 8));
        _blocks.ListByDoctorAsync(DoctorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability> { before, first, closed, last, after });

        var result = await Handle(new GetAvailabilityBlocksQuery(January1, new DateOnly(2027, 1, 31)));

        result.Value.Select(block => block.Id).ShouldBe(new[] { 2, 3, 4 });
        result.Value[0].ShouldBe(new AvailabilityBlockDto(2, 15, first.StartsAt, first.EndsAt, true, false, false));
        result.Value[1].IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Indica_si_cada_bloque_todavia_puede_reprogramarse_segun_la_regla_de_semanas()
    {
        // Jueves 7 de enero de 2027: la semana siguiente ya no se reprograma; la de después, sí.
        _clock.UtcNow = Local(2027, 1, 7, 10);
        var currentWeek = Block(1, 15, Local(2027, 1, 8, 8));
        var nextWeek = Block(2, 15, Local(2027, 1, 12, 8));
        var later = Block(3, null, Local(2027, 1, 19, 8));
        var closed = Block(4, 15, Local(2027, 1, 20, 8));
        closed.Close(null, _clock.UtcNow).IsSuccess.ShouldBeTrue();
        _blocks.ListByDoctorAsync(DoctorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability> { currentWeek, nextWeek, later, closed });

        var result = await Handle(new GetAvailabilityBlocksQuery(new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 24)));

        result.Value.Select(block => (block.Id, block.CanReschedule))
            .ShouldBe(new[] { (1, false), (2, false), (3, true), (4, false) });
    }

    [Fact]
    public async Task Un_rango_invalido_se_rechaza_sin_consultar()
    {
        var result = await Handle(new GetAvailabilityBlocksQuery(January1, January1.AddDays(31)));

        result.Error.Code.ShouldBe("Availability.DateRangeTooLong");
        _blocks.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Sin_perfil_de_medico_activo_responde_medico_no_encontrado()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        (await Handle(new GetAvailabilityBlocksQuery(January1, January1))).Error.ShouldBe(DoctorErrors.NotFound);
    }

    private Task<Result<IReadOnlyList<AvailabilityBlockDto>>> Handle(GetAvailabilityBlocksQuery query) =>
        new GetAvailabilityBlocksQueryHandler(
                _currentUser, _doctors, _blocks,
                BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone), _clock, _validator)
            .HandleAsync(query, CancellationToken.None);

    private static DoctorAvailability Block(int id, int? seriesId, DateTimeOffset startsAt)
    {
        var block = DoctorAvailability.Create(DoctorId, seriesId, startsAt, startsAt.AddHours(6), 6).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(block, id);
        return block;
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, SantoDomingo);
}
