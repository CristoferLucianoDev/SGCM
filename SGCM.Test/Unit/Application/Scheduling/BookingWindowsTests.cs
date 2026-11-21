using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Scheduling;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class BookingWindowsTests
{
    private const string HavanaTimeZone = "America/Havana";

    private static readonly BusinessCalendar SantoDomingo = BusinessCalendar.ForTimeZone(DoctorAvailabilitySeriesMother.TimeZone);

    private static readonly SchedulingOptions Options = new()
    {
        TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
        MinimumBookingLeadDays = 4,
        BookingHorizonDays = 90
    };

    // Santo Domingo es UTC-4 todo el año. Lunes 4 de enero de 2027 a las 12:00 locales.
    private static readonly DateTimeOffset NoonOnJanuaryFourth = new(2027, 1, 4, 16, 0, 0, TimeSpan.Zero);

    // 4 de enero + 4 días = 8 de enero, a las 00:00 locales.
    private static readonly DateTimeOffset PatientStart = new(2027, 1, 8, 4, 0, 0, TimeSpan.Zero);

    // 4 de enero + 90 días = 4 de abril; el día del horizonte entra completo, así que el fin es el 5 de abril a las 00:00 locales.
    private static readonly DateTimeOffset WindowEnd = new(2027, 4, 5, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForPatient_empieza_a_las_00_locales_del_dia_de_la_antelacion_y_termina_al_final_del_horizonte()
    {
        var window = BookingWindows.ForPatient(NoonOnJanuaryFourth, SantoDomingo, Options);

        window.Start.ShouldBe(PatientStart);
        window.End.ShouldBe(WindowEnd);
        window.Start.Offset.ShouldBe(TimeSpan.Zero);
        window.End.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ForPatient_con_un_ahora_que_ya_es_el_dia_siguiente_en_UTC_usa_la_fecha_local()
    {
        // 4 de enero a las 23:00 en Santo Domingo, que en UTC ya es el 5 de enero a las 03:00.
        var lateNight = new DateTimeOffset(2027, 1, 5, 3, 0, 0, TimeSpan.Zero);

        var window = BookingWindows.ForPatient(lateNight, SantoDomingo, Options);

        window.Start.ShouldBe(PatientStart);
        window.End.ShouldBe(WindowEnd);
    }

    [Fact]
    public void ForPatient_sin_antelacion_empieza_en_el_instante_actual_y_no_en_la_medianoche()
    {
        var options = new SchedulingOptions
        {
            TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
            MinimumBookingLeadDays = 0,
            BookingHorizonDays = 90
        };

        var window = BookingWindows.ForPatient(NoonOnJanuaryFourth, SantoDomingo, options);

        window.Start.ShouldBe(NoonOnJanuaryFourth);
        window.End.ShouldBe(WindowEnd);
    }

    [Fact]
    public void ForPatient_sin_antelacion_y_ahora_en_la_medianoche_local_empieza_en_esa_medianoche()
    {
        var options = new SchedulingOptions
        {
            TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
            MinimumBookingLeadDays = 0,
            BookingHorizonDays = 90
        };
        // 4 de enero a las 00:00 en Santo Domingo (UTC-4).
        var localMidnight = new DateTimeOffset(2027, 1, 4, 4, 0, 0, TimeSpan.Zero);

        var window = BookingWindows.ForPatient(localMidnight, SantoDomingo, options);

        window.Start.ShouldBe(localMidnight);
        window.End.ShouldBe(WindowEnd);
    }

    [Fact]
    public void ForDoctor_empieza_en_el_instante_dado_aunque_tenga_minutos_y_termina_como_la_del_paciente()
    {
        var withMinutes = NoonOnJanuaryFourth.AddMinutes(37);

        var window = BookingWindows.ForDoctor(withMinutes, SantoDomingo, Options);

        window.Start.ShouldBe(withMinutes);
        window.End.ShouldBe(BookingWindows.ForPatient(withMinutes, SantoDomingo, Options).End);
        window.End.ShouldBe(WindowEnd);
    }

    [Fact]
    public void ForPatient_si_la_medianoche_no_existe_empieza_en_la_primera_hora_local_que_existe()
    {
        // En La Habana el reloj pasa de las 00:00 a las 01:00 el domingo 14 de marzo de 2027 (UTC-5 a UTC-4),
        // así que ese día no hay 00:00 y la primera hora local es la 01:00, es decir, las 05:00 UTC.
        // Miércoles 10 de marzo de 2027 a las 12:00 locales (UTC-5, 17:00 UTC) + 4 días = 14 de marzo.
        var havana = BusinessCalendar.ForTimeZone(HavanaTimeZone);
        var now = new DateTimeOffset(2027, 3, 10, 17, 0, 0, TimeSpan.Zero);

        var window = BookingWindows.ForPatient(now, havana, Options);

        window.Start.ShouldBe(new DateTimeOffset(2027, 3, 14, 5, 0, 0, TimeSpan.Zero));
        // 10 de marzo + 90 días = 8 de junio; el fin es el 9 de junio a las 00:00 locales (UTC-4).
        window.End.ShouldBe(new DateTimeOffset(2027, 6, 9, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ForPatient_si_la_medianoche_del_fin_no_existe_termina_en_la_primera_hora_local_que_existe()
    {
        // Hoy 10 de marzo de 2027 (La Habana) con un horizonte de 3 días: el día del horizonte es el 13 y el fin
        // cae el 14 de marzo, que no tiene 00:00. La antelación de 1 día empieza el 11 de marzo a las 00:00
        // locales (UTC-5, 05:00 UTC).
        var havana = BusinessCalendar.ForTimeZone(HavanaTimeZone);
        var options = new SchedulingOptions
        {
            TimeZone = HavanaTimeZone,
            MinimumBookingLeadDays = 1,
            BookingHorizonDays = 3
        };
        var now = new DateTimeOffset(2027, 3, 10, 17, 0, 0, TimeSpan.Zero);

        var window = BookingWindows.ForPatient(now, havana, options);

        window.Start.ShouldBe(new DateTimeOffset(2027, 3, 11, 5, 0, 0, TimeSpan.Zero));
        window.End.ShouldBe(new DateTimeOffset(2027, 3, 14, 5, 0, 0, TimeSpan.Zero));
    }
}
