using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Domain.Scheduling.Availability.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilityRescheduleTests
{
    private const int DoctorId = DoctorAvailabilitySeriesMother.DoctorId;
    private const int MinimumHours = DoctorAvailabilitySeriesMother.MinimumDurationHours;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);
    private static readonly BusinessCalendar Calendar = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);

    // Lunes 21 de diciembre de 2026 a las 7:30 en Santo Domingo; el bloque es el martes 5 de enero de 8:00 a 14:00,
    // dos semanas después, así que la regla de semanas permite reprogramarlo.
    private static readonly DateTimeOffset Now = new(2026, 12, 21, 7, 30, 0, SantoDomingo);
    private static readonly DateTimeOffset Start = new(2027, 1, 5, 8, 0, 0, SantoDomingo);

    [Fact]
    public void Reprogramar_cambia_el_horario_en_UTC_sin_tocar_los_demas_datos()
    {
        var block = Occurrence();

        var result = block.Reschedule(Start.AddHours(2), Start.AddHours(8), MinimumHours, Now, Calendar);

        result.IsSuccess.ShouldBeTrue();
        block.StartsAt.ShouldBe(new DateTimeOffset(2027, 1, 5, 14, 0, 0, TimeSpan.Zero));
        block.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        block.EndsAt.ShouldBe(new DateTimeOffset(2027, 1, 5, 20, 0, 0, TimeSpan.Zero));
        block.DoctorId.ShouldBe(DoctorId);
        block.SeriesId.ShouldBe(15);
        block.IsAvailable.ShouldBeTrue();
        block.IsModified.ShouldBeFalse();
        // De 8:00 a 14:00 pasa a 10:00 a 16:00: deja de cubrir de 8:00 a 10:00.
        var rescheduled = block.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DoctorAvailabilityRescheduledEvent>();
        rescheduled.DoctorId.ShouldBe(DoctorId);
        rescheduled.RemovedRanges.ShouldBe(new[] { TimeRange.Create(Start, Start.AddHours(2)).Value });
        rescheduled.Reason.ShouldBeNull();
    }

    [Fact]
    public void Un_bloque_suelto_tambien_se_reprograma_a_otro_dia()
    {
        var block = Loose();

        block.Reschedule(Start.AddDays(3), Start.AddDays(3).AddHours(6), MinimumHours, Now, Calendar).IsSuccess.ShouldBeTrue();

        block.StartsAt.ShouldBe(Start.AddDays(3));
        block.IsModified.ShouldBeFalse();
    }

    [Theory]
    [InlineData(30, 6, "Availability.NotOnHour")]
    [InlineData(0, 0, "Availability.InvalidTimeRange")]
    [InlineData(0, -2, "Availability.InvalidTimeRange")]
    [InlineData(0, 5, "Availability.DurationTooShort")]
    public void Reprogramar_aplica_las_reglas_de_horario_sin_cambiar_el_bloque(int startMinutes, int hours, string code)
    {
        var block = Occurrence();
        var newStart = Start.AddMinutes(startMinutes);

        AssertRejectedWithoutChanges(block, block.Reschedule(newStart, newStart.AddHours(hours), MinimumHours, Now, Calendar), code);
    }

    [Fact]
    public void La_duracion_minima_sale_de_la_configuracion()
    {
        var block = Occurrence();

        AssertRejectedWithoutChanges(block, block.Reschedule(Start, Start.AddHours(6), 8, Now, Calendar), "Availability.DurationTooShort");
    }

    [Fact]
    public void No_se_reprograma_a_un_horario_pasado()
    {
        var block = Occurrence();
        var past = new DateTimeOffset(2026, 12, 21, 7, 0, 0, SantoDomingo);

        AssertRejectedWithoutChanges(block, block.Reschedule(past, past.AddHours(6), MinimumHours, Now, Calendar), "Availability.StartsInPast");
    }

    [Fact]
    public void Un_bloque_que_ya_empezo_no_se_reprograma()
    {
        var block = Occurrence();
        var during = Start.AddHours(1);

        AssertRejectedWithoutChanges(block,
            block.Reschedule(Start.AddDays(1), Start.AddDays(1).AddHours(6), MinimumHours, during, Calendar), "Availability.AlreadyStarted");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_minima_no_positiva_es_un_error_de_programacion(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Occurrence().Reschedule(Start, Start.AddHours(6), hours, Now, Calendar));
    }

    [Fact]
    public void Editar_solo_esta_ocurrencia_cambia_el_horario_y_la_marca_como_modificada()
    {
        var block = Occurrence();

        var result = block.RescheduleOccurrence(Start.AddHours(2), Start.AddHours(8), MinimumHours, Now, Calendar);

        result.IsSuccess.ShouldBeTrue();
        block.StartsAt.ShouldBe(new DateTimeOffset(2027, 1, 5, 14, 0, 0, TimeSpan.Zero));
        block.EndsAt.ShouldBe(new DateTimeOffset(2027, 1, 5, 20, 0, 0, TimeSpan.Zero));
        block.IsModified.ShouldBeTrue();
        block.SeriesId.ShouldBe(15);
        block.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void Una_ocurrencia_ya_modificada_se_puede_volver_a_editar()
    {
        var block = Occurrence();
        block.RescheduleOccurrence(Start.AddHours(2), Start.AddHours(8), MinimumHours, Now, Calendar).IsSuccess.ShouldBeTrue();

        block.RescheduleOccurrence(Start.AddHours(-2), Start.AddHours(4), MinimumHours, Now, Calendar).IsSuccess.ShouldBeTrue();

        block.StartsAt.ShouldBe(Start.AddHours(-2));
        block.IsModified.ShouldBeTrue();
    }

    [Fact]
    public void Un_bloque_suelto_no_se_edita_como_ocurrencia()
    {
        var block = Loose();

        AssertRejectedWithoutChanges(block,
            block.RescheduleOccurrence(Start.AddHours(2), Start.AddHours(8), MinimumHours, Now, Calendar),
            "Availability.NotAnOccurrence");
    }

    [Fact]
    public void Una_ocurrencia_no_se_mueve_a_otro_dia()
    {
        var block = Occurrence();

        AssertRejectedWithoutChanges(block,
            block.RescheduleOccurrence(Start.AddDays(1), Start.AddDays(1).AddHours(6), MinimumHours, Now, Calendar),
            "Availability.OccurrenceDayChanged");
    }

    [Fact]
    public void El_dia_de_la_ocurrencia_se_decide_en_la_hora_local()
    {
        // 20:00 del martes en Santo Domingo ya es miércoles en UTC, pero sigue siendo el mismo día local.
        var block = Occurrence();
        var evening = new DateTimeOffset(2027, 1, 5, 16, 0, 0, SantoDomingo);

        block.RescheduleOccurrence(evening, evening.AddHours(6), MinimumHours, Now, Calendar).IsSuccess.ShouldBeTrue();

        block.StartsAt.UtcDateTime.Day.ShouldBe(5);
        block.EndsAt.UtcDateTime.Day.ShouldBe(6);
    }

    [Theory]
    [InlineData(30, 6, "Availability.NotOnHour")]
    [InlineData(0, 5, "Availability.DurationTooShort")]
    public void Una_edicion_invalida_no_cambia_ni_marca_la_ocurrencia(int startMinutes, int hours, string code)
    {
        var block = Occurrence();
        var newStart = Start.AddMinutes(startMinutes);

        AssertRejectedWithoutChanges(block,
            block.RescheduleOccurrence(newStart, newStart.AddHours(hours), MinimumHours, Now, Calendar), code);
    }

    [Fact]
    public void Una_ocurrencia_que_ya_empezo_no_se_edita()
    {
        var block = Occurrence();

        AssertRejectedWithoutChanges(block,
            block.RescheduleOccurrence(Start.AddHours(2), Start.AddHours(8), MinimumHours, Start.AddHours(1), Calendar),
            "Availability.AlreadyStarted");
    }

    private static DoctorAvailability Occurrence() =>
        DoctorAvailability.Create(DoctorId, 15, Start, Start.AddHours(6), MinimumHours).Value;

    private static DoctorAvailability Loose() =>
        DoctorAvailability.CreateLoose(DoctorId, Start, Start.AddHours(6), MinimumHours, Now).Value;

    private static void AssertRejectedWithoutChanges(DoctorAvailability block, Result result, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        block.StartsAt.ShouldBe(Start);
        block.EndsAt.ShouldBe(Start.AddHours(6));
        block.IsModified.ShouldBeFalse();
        block.DomainEvents.ShouldBeEmpty();
    }
}
