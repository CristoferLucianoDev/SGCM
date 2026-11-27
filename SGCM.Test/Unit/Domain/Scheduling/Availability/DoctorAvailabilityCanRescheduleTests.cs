using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilityCanRescheduleTests
{
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly BusinessCalendar Calendar = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);
    private static readonly DateTimeOffset Tuesday = Local(5, 10);
    private static readonly DateTimeOffset Thursday = Local(7, 10);

    [Fact]
    public void Un_bloque_de_la_semana_actual_no_puede_reprogramarse()
    {
        Block(Local(8, 8)).CanReschedule(Tuesday, Calendar).ShouldBeFalse();
    }

    [Fact]
    public void Un_bloque_de_la_semana_siguiente_puede_reprogramarse_de_lunes_a_miercoles()
    {
        var block = Block(Local(13, 8));

        block.CanReschedule(Tuesday, Calendar).ShouldBeTrue();
        block.CanReschedule(Thursday, Calendar).ShouldBeFalse();
    }

    [Fact]
    public void Un_bloque_de_semanas_posteriores_puede_reprogramarse_cualquier_dia()
    {
        Block(Local(20, 8)).CanReschedule(Thursday, Calendar).ShouldBeTrue();
    }

    [Fact]
    public void Un_bloque_cerrado_no_puede_reprogramarse()
    {
        var block = Block(Local(20, 8));
        block.Close(null, Tuesday).IsSuccess.ShouldBeTrue();

        block.CanReschedule(Tuesday, Calendar).ShouldBeFalse();
    }

    [Fact]
    public void Un_bloque_que_ya_empezo_no_puede_reprogramarse()
    {
        var block = Block(Local(20, 8));

        block.CanReschedule(Local(20, 9), Calendar).ShouldBeFalse();
    }

    private static DoctorAvailability Block(DateTimeOffset start) =>
        DoctorAvailability.Create(DoctorAvailabilitySeriesMother.DoctorId, 15, start, start.AddHours(6), 6).Value;

    private static DateTimeOffset Local(int day, int hour) => new(2027, 1, day, hour, 0, 0, SantoDomingo);
}
