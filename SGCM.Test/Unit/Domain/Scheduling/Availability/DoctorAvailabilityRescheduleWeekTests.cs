using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Domain.Scheduling.Availability.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilityRescheduleWeekTests
{
    private const int DoctorId = DoctorAvailabilitySeriesMother.DoctorId;
    private const int BlockId = 40;
    private const int MinimumHours = DoctorAvailabilitySeriesMother.MinimumDurationHours;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly BusinessCalendar Calendar = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);

    // Hoy es de la semana del lunes 4 de enero de 2027.
    private static readonly DateTimeOffset Tuesday = new(2027, 1, 5, 10, 0, 0, SantoDomingo);
    private static readonly DateTimeOffset Thursday = new(2027, 1, 7, 10, 0, 0, SantoDomingo);

    [Fact]
    public void Un_bloque_de_la_semana_actual_no_se_reprograma_y_solo_queda_cerrarlo()
    {
        var block = BlockOn(new DateTimeOffset(2027, 1, 8, 8, 0, 0, SantoDomingo));

        AssertRejected(block, b => b.Reschedule(block.StartsAt.AddHours(2), block.EndsAt.AddHours(2), MinimumHours, Tuesday, Calendar),
            SchedulingErrors.CurrentWeekLocked);
    }

    [Fact]
    public void Un_martes_se_reprograma_un_bloque_de_la_semana_siguiente()
    {
        var block = BlockOn(new DateTimeOffset(2027, 1, 13, 8, 0, 0, SantoDomingo));

        block.Reschedule(block.StartsAt.AddHours(2), block.EndsAt.AddHours(2), MinimumHours, Tuesday, Calendar)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Un_jueves_no_se_reprograma_un_bloque_de_la_semana_siguiente()
    {
        var block = BlockOn(new DateTimeOffset(2027, 1, 13, 8, 0, 0, SantoDomingo));

        AssertRejected(block, b => b.Reschedule(block.StartsAt.AddHours(2), block.EndsAt.AddHours(2), MinimumHours, Thursday, Calendar),
            SchedulingErrors.NextWeekLocked);
    }

    [Fact]
    public void Un_jueves_se_reprograma_un_bloque_de_las_semanas_posteriores()
    {
        var block = BlockOn(new DateTimeOffset(2027, 1, 20, 8, 0, 0, SantoDomingo));

        block.Reschedule(block.StartsAt.AddHours(2), block.EndsAt.AddHours(2), MinimumHours, Thursday, Calendar)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Tampoco_se_puede_mover_un_bloque_lejano_a_la_semana_actual()
    {
        var block = BlockOn(new DateTimeOffset(2027, 1, 20, 8, 0, 0, SantoDomingo));
        var friday = new DateTimeOffset(2027, 1, 8, 8, 0, 0, SantoDomingo);

        AssertRejected(block, b => b.Reschedule(friday, friday.AddHours(6), MinimumHours, Tuesday, Calendar),
            SchedulingErrors.CurrentWeekLocked);
    }

    [Fact]
    public void Reprogramar_emite_el_evento_con_los_rangos_que_dejan_de_estar_disponibles_y_el_motivo()
    {
        var start = new DateTimeOffset(2027, 1, 20, 8, 0, 0, SantoDomingo);
        var block = BlockOn(start, hours: 10);

        // De 8:00 a 18:00 pasa a 10:00 a 16:00: deja de cubrir de 8:00 a 10:00 y de 16:00 a 18:00.
        block.Reschedule(start.AddHours(2), start.AddHours(8), MinimumHours, Tuesday, Calendar, "  Cambio de consultorio  ")
            .IsSuccess.ShouldBeTrue();

        var domainEvent = block.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DoctorAvailabilityRescheduledEvent>();
        domainEvent.BlockId.ShouldBe(BlockId);
        domainEvent.DoctorId.ShouldBe(DoctorId);
        domainEvent.RemovedRanges.ShouldBe(new[] { Range(start, start.AddHours(2)), Range(start.AddHours(8), start.AddHours(10)) });
        domainEvent.Reason.ShouldBe("Cambio de consultorio");
    }

    [Fact]
    public void Ampliar_el_horario_no_quita_rangos_y_no_emite_evento()
    {
        var start = new DateTimeOffset(2027, 1, 20, 8, 0, 0, SantoDomingo);
        var block = BlockOn(start);

        block.Reschedule(start.AddHours(-1), start.AddHours(7), MinimumHours, Tuesday, Calendar).IsSuccess.ShouldBeTrue();

        block.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Mover_el_bloque_a_otro_dia_quita_todo_el_horario_anterior()
    {
        var start = new DateTimeOffset(2027, 1, 20, 8, 0, 0, SantoDomingo);
        var block = BlockOn(start);

        block.Reschedule(start.AddDays(1), start.AddDays(1).AddHours(6), MinimumHours, Tuesday, Calendar).IsSuccess.ShouldBeTrue();

        block.DomainEvents.OfType<DoctorAvailabilityRescheduledEvent>().ShouldHaveSingleItem()
            .RemovedRanges.ShouldBe(new[] { Range(start, start.AddHours(6)) });
    }

    [Fact]
    public void Editar_solo_esta_ocurrencia_tambien_sigue_la_regla_de_semanas()
    {
        var block = BlockOn(new DateTimeOffset(2027, 1, 13, 8, 0, 0, SantoDomingo));

        AssertRejected(block,
            b => b.RescheduleOccurrence(block.StartsAt.AddHours(2), block.EndsAt.AddHours(2), MinimumHours, Thursday, Calendar),
            SchedulingErrors.NextWeekLocked);
        block.IsModified.ShouldBeFalse();
    }

    [Fact]
    public void Un_calendario_nulo_es_un_error_de_programacion()
    {
        var block = BlockOn(new DateTimeOffset(2027, 1, 20, 8, 0, 0, SantoDomingo));

        Should.Throw<ArgumentNullException>(() =>
            block.Reschedule(block.StartsAt.AddHours(1), block.EndsAt.AddHours(1), MinimumHours, Tuesday, null!));
    }

    private static void AssertRejected(DoctorAvailability block, Func<DoctorAvailability, Result> reschedule, Error error)
    {
        var original = (block.StartsAt, block.EndsAt);

        var result = reschedule(block);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        (block.StartsAt, block.EndsAt).ShouldBe(original);
        block.DomainEvents.ShouldBeEmpty();
    }

    private static DoctorAvailability BlockOn(DateTimeOffset start, int hours = MinimumHours)
    {
        var block = DoctorAvailability.Create(DoctorId, 15, start, start.AddHours(hours), MinimumHours).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(block, BlockId);
        return block;
    }

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) => TimeRange.Create(start, end).Value;
}

public sealed class DoctorAvailabilityValidateRescheduleTests
{
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly BusinessCalendar Calendar = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);
    private static readonly DateTimeOffset Tuesday = new(2027, 1, 5, 10, 0, 0, SantoDomingo);
    private static readonly DateTimeOffset Start = new(2027, 1, 20, 8, 0, 0, SantoDomingo);

    [Fact]
    public void Un_horario_valido_no_tiene_error_y_el_bloque_no_cambia()
    {
        var block = Block();

        block.ValidateReschedule(Start.AddHours(2), Start.AddHours(8), 6, Tuesday, Calendar).ShouldBe(Error.None);
        block.StartsAt.ShouldBe(Start);
        block.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_bloque_cerrado_no_se_reprograma()
    {
        var block = Block();
        block.Close(null, Tuesday);
        block.ClearDomainEvents();

        block.ValidateReschedule(Start.AddHours(2), Start.AddHours(8), 6, Tuesday, Calendar)
            .ShouldBe(DoctorAvailabilityErrors.AlreadyClosed);
        block.Reschedule(Start.AddHours(2), Start.AddHours(8), 6, Tuesday, Calendar).Error
            .ShouldBe(DoctorAvailabilityErrors.AlreadyClosed);
        block.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Devuelve_los_mismos_errores_que_Reschedule()
    {
        var block = Block();
        var friday = new DateTimeOffset(2027, 1, 8, 8, 0, 0, SantoDomingo);

        block.ValidateReschedule(Start.AddMinutes(30), Start.AddHours(7), 6, Tuesday, Calendar).ShouldBe(DoctorAvailabilityErrors.NotOnHour);
        block.ValidateReschedule(Start, Start.AddHours(5), 6, Tuesday, Calendar).ShouldBe(DoctorAvailabilityErrors.DurationTooShort);
        block.ValidateReschedule(friday, friday.AddHours(6), 6, Tuesday, Calendar).ShouldBe(SchedulingErrors.CurrentWeekLocked);
    }

    private static DoctorAvailability Block() =>
        DoctorAvailability.Create(DoctorAvailabilitySeriesMother.DoctorId, 15, Start, Start.AddHours(6), 6).Value;
}
