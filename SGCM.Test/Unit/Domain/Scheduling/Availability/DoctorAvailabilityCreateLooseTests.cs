using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilityCreateLooseTests
{
    private const int DoctorId = DoctorAvailabilitySeriesMother.DoctorId;
    private const int MinimumHours = DoctorAvailabilitySeriesMother.MinimumDurationHours;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    // Lunes 4 de enero de 2027 a las 7:30 en Santo Domingo.
    private static readonly DateTimeOffset Now = new(2027, 1, 4, 7, 30, 0, SantoDomingo);
    private static readonly DateTimeOffset Start = new(2027, 1, 5, 8, 0, 0, SantoDomingo);

    [Fact]
    public void Un_bloque_suelto_futuro_queda_sin_serie_abierto_y_sin_marca_de_modificado()
    {
        var result = DoctorAvailability.CreateLoose(DoctorId, Start, Start.AddHours(6), MinimumHours, Now);

        result.IsSuccess.ShouldBeTrue();
        var block = result.Value;
        block.DoctorId.ShouldBe(DoctorId);
        block.SeriesId.ShouldBeNull();
        block.IsAvailable.ShouldBeTrue();
        block.IsModified.ShouldBeFalse();
        block.StartsAt.ShouldBe(new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero));
        block.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        block.EndsAt.ShouldBe(new DateTimeOffset(2027, 1, 5, 18, 0, 0, TimeSpan.Zero));
        block.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_bloque_de_mas_tarde_hoy_tambien_es_futuro()
    {
        var today = new DateTimeOffset(2027, 1, 4, 8, 0, 0, SantoDomingo);

        DoctorAvailability.CreateLoose(DoctorId, today, today.AddHours(6), MinimumHours, Now).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-24)]
    [InlineData(-1)]
    public void No_se_permite_crear_un_bloque_en_una_fecha_pasada(int hoursFromStart)
    {
        var start = new DateTimeOffset(2027, 1, 4, 7, 0, 0, SantoDomingo).AddHours(hoursFromStart + 1);

        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, start, start.AddHours(6), MinimumHours, Now),
            DoctorAvailabilityErrors.StartsInPast);
    }

    [Fact]
    public void Un_bloque_que_ya_empezo_aunque_termine_en_el_futuro_se_rechaza()
    {
        var started = new DateTimeOffset(2027, 1, 4, 7, 0, 0, SantoDomingo);

        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, started, started.AddHours(6), MinimumHours, Now),
            DoctorAvailabilityErrors.StartsInPast);
    }

    [Fact]
    public void Un_bloque_que_empieza_justo_ahora_no_es_futuro()
    {
        var now = new DateTimeOffset(2027, 1, 4, 8, 0, 0, SantoDomingo);

        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, now, now.AddHours(6), MinimumHours, now),
            DoctorAvailabilityErrors.StartsInPast);
    }

    [Fact]
    public void El_futuro_se_compara_por_instante_sin_importar_el_desfase()
    {
        // 11:00 UTC son las 7:00 en Santo Domingo: antes de Now (7:30).
        var startUtc = new DateTimeOffset(2027, 1, 4, 11, 0, 0, TimeSpan.Zero);

        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, startUtc, startUtc.AddHours(6), MinimumHours, Now),
            DoctorAvailabilityErrors.StartsInPast);
        DoctorAvailability.CreateLoose(DoctorId, startUtc.AddHours(1), startUtc.AddHours(7), MinimumHours, Now)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Las_horas_van_en_punto() =>
        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, Start.AddMinutes(30), Start.AddHours(7), MinimumHours, Now),
            DoctorAvailabilityErrors.NotOnHour);

    [Fact]
    public void La_duracion_minima_es_la_de_la_configuracion()
    {
        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, Start, Start.AddHours(MinimumHours - 1), MinimumHours, Now),
            DoctorAvailabilityErrors.DurationTooShort);
        DoctorAvailability.CreateLoose(DoctorId, Start, Start.AddHours(MinimumHours), MinimumHours, Now)
            .IsSuccess.ShouldBeTrue();
        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, Start, Start.AddHours(6), 8, Now),
            DoctorAvailabilityErrors.DurationTooShort);
    }

    [Fact]
    public void Un_fin_anterior_o_igual_al_inicio_se_rechaza()
    {
        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, Start, Start, MinimumHours, Now),
            DoctorAvailabilityErrors.InvalidTimeRange);
        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, Start, Start.AddHours(-6), MinimumHours, Now),
            DoctorAvailabilityErrors.InvalidTimeRange);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_medico_no_positivo_se_rechaza(int doctorId) =>
        AssertRejected(DoctorAvailability.CreateLoose(doctorId, Start, Start.AddHours(6), MinimumHours, Now),
            DoctorAvailabilityErrors.InvalidDoctor);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_minima_no_positiva_es_un_error_de_programacion(int hours)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => DoctorAvailability.CreateLoose(DoctorId, Start, Start.AddHours(6), hours, Now));
    }

    [Fact]
    public void Una_fecha_pasada_con_otro_error_reporta_primero_el_error_de_forma()
    {
        var past = new DateTimeOffset(2027, 1, 3, 8, 30, 0, SantoDomingo);

        AssertRejected(DoctorAvailability.CreateLoose(DoctorId, past, past.AddHours(6), MinimumHours, Now),
            DoctorAvailabilityErrors.NotOnHour);
    }

    private static void AssertRejected(Result<DoctorAvailability> result, Error expected)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(expected);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }
}
