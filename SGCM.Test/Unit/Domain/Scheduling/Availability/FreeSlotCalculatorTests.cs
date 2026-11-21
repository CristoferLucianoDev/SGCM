using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

/// <summary>Pruebas del cálculo de horarios libres: bloques abiertos, citas activas, ventana y cita excluida.</summary>
public sealed class FreeSlotCalculatorTests
{
    private const int TwoHours = 2;
    private static readonly DateTimeOffset Day = new(2027, 1, 4, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double hour) => Day.AddHours(hour);

    private static TimeRange Range(double startHour, double endHour) =>
        TimeRange.Create(At(startHour), At(endHour)).Value;

    private static TimeRange WideWindow() => Range(0, 72);

    private static BookedInterval Booked(int id, double startHour, double endHour) =>
        new(id, Range(startHour, endHour));

    private static DateTimeOffset[] Hours(params double[] hours) => hours.Select(At).ToArray();

    private static IReadOnlyList<DateTimeOffset> Calculate(
        TimeRange[] blocks,
        BookedInterval[] appointments,
        TimeRange? window = null,
        int duration = TwoHours,
        int? excludedId = null) =>
        FreeSlotCalculator.Calculate(blocks, appointments, window ?? WideWindow(), duration, excludedId);

    [Fact]
    public void El_ejemplo_del_PBI_ofrece_8_12_13_y_14()
    {
        var result = Calculate([Range(8, 16)], [Booked(1, 10, 12)]);

        result.ShouldBe(Hours(8, 12, 13, 14));
    }

    [Fact]
    public void Sin_citas_un_bloque_de_8_horas_ofrece_7_inicios()
    {
        var result = Calculate([Range(8, 16)], []);

        result.ShouldBe(Hours(8, 9, 10, 11, 12, 13, 14));
    }

    [Fact]
    public void Un_bloque_de_exactamente_2_horas_ofrece_un_solo_horario()
    {
        Calculate([Range(8, 10)], []).ShouldBe(Hours(8));
    }

    [Fact]
    public void Un_bloque_de_1_hora_no_ofrece_horarios()
    {
        Calculate([Range(8, 9)], []).ShouldBeEmpty();
    }

    [Fact]
    public void Un_bloque_que_empieza_a_media_hora_ofrece_desde_la_siguiente_hora_en_punto()
    {
        Calculate([Range(8.5, 12.5)], []).ShouldBe(Hours(9, 10));
    }

    [Fact]
    public void El_ultimo_inicio_que_deja_menos_de_2_horas_antes_del_fin_del_bloque_no_se_ofrece()
    {
        var result = Calculate([Range(8, 16)], []);

        result.ShouldNotContain(At(15));
        result.Last().ShouldBe(At(14));
    }

    [Fact]
    public void Una_cita_que_termina_cuando_empieza_el_bloque_no_bloquea_el_primer_horario()
    {
        var result = Calculate([Range(8, 16)], [Booked(1, 6, 8)]);

        result.ShouldBe(Hours(8, 9, 10, 11, 12, 13, 14));
    }

    [Fact]
    public void Una_cita_que_empieza_cuando_termina_el_bloque_no_bloquea_el_ultimo_horario()
    {
        var result = Calculate([Range(8, 16)], [Booked(1, 16, 18)]);

        result.ShouldContain(At(14));
        result.Count.ShouldBe(7);
    }

    [Fact]
    public void Una_cita_con_fin_fuera_de_hora_bloquea_hasta_su_fin_y_el_siguiente_libre_es_las_12()
    {
        var result = Calculate([Range(8, 16)], [Booked(1, 10, 11.5)]);

        result.ShouldBe(Hours(8, 12, 13, 14));
    }

    [Fact]
    public void Una_cita_que_cubre_todo_el_bloque_no_deja_horarios()
    {
        Calculate([Range(8, 16)], [Booked(1, 8, 16)]).ShouldBeEmpty();
    }

    [Fact]
    public void El_resultado_sale_ordenado_aunque_bloques_y_citas_lleguen_desordenados()
    {
        var result = Calculate(
            [Range(32, 36), Range(8, 16)],
            [Booked(2, 13, 14), Booked(1, 9, 10)]);

        result.ShouldBe(Hours(10, 11, 14, 32, 33, 34));
    }

    [Fact]
    public void Dos_bloques_contiguos_no_se_unen_y_no_se_ofrece_un_horario_que_cruce_la_union()
    {
        var result = Calculate([Range(8, 12), Range(12, 16)], []);

        result.ShouldBe(Hours(8, 9, 10, 12, 13, 14));
        result.ShouldNotContain(At(11));
    }

    [Fact]
    public void Dos_bloques_traslapados_entre_si_no_repiten_instantes()
    {
        var result = Calculate([Range(8, 12), Range(10, 14)], []);

        result.ShouldBe(Hours(8, 9, 10, 11, 12));
    }

    [Fact]
    public void La_ventana_que_empieza_a_las_10_solo_ofrece_horarios_desde_las_10()
    {
        var result = Calculate([Range(8, 16)], [], Range(10, 72));

        result.ShouldBe(Hours(10, 11, 12, 13, 14));
    }

    [Fact]
    public void La_ventana_que_empieza_a_las_10_30_ofrece_primero_el_horario_de_las_11()
    {
        var result = Calculate([Range(8, 16)], [], Range(10.5, 72));

        result.ShouldBe(Hours(11, 12, 13, 14));
    }

    [Fact]
    public void El_fin_de_la_ventana_es_exclusivo_y_el_inicio_es_inclusivo()
    {
        var result = Calculate([Range(8, 16)], [], Range(8, 12));

        result.ShouldBe(Hours(8, 9, 10, 11));
        result.ShouldNotContain(At(12));
    }

    [Fact]
    public void Un_horario_que_empieza_dentro_de_la_ventana_y_termina_despues_de_ella_si_se_ofrece()
    {
        var result = Calculate([Range(8, 16)], [], Range(10, 12));

        result.ShouldBe(Hours(10, 11));
    }

    [Fact]
    public void La_cita_excluida_libera_su_propio_horario_para_reprogramarla()
    {
        var result = Calculate([Range(8, 16)], [Booked(1, 10, 12), Booked(2, 14, 16)], excludedId: 1);

        result.ShouldBe(Hours(8, 9, 10, 11, 12));
    }

    [Fact]
    public void Excluir_el_id_de_otra_cita_no_cambia_el_resultado()
    {
        var result = Calculate([Range(8, 16)], [Booked(1, 10, 12), Booked(2, 14, 16)], excludedId: 99);

        result.ShouldBe(Hours(8, 12));
    }

    [Fact]
    public void Las_citas_no_excluidas_siguen_bloqueando()
    {
        var result = Calculate([Range(8, 16)], [Booked(1, 10, 12), Booked(2, 14, 16)], excludedId: 1);

        result.ShouldNotContain(At(13));
        result.ShouldNotContain(At(14));
    }

    [Fact]
    public void Una_duracion_de_3_horas_se_respeta()
    {
        var result = Calculate([Range(8, 16)], [], duration: 3);

        result.ShouldBe(Hours(8, 9, 10, 11, 12, 13));
    }

    [Fact]
    public void Sin_bloques_devuelve_una_lista_vacia_no_nula()
    {
        var result = Calculate([], [Booked(1, 10, 12)]);

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public void Los_resultados_salen_en_UTC_aunque_los_rangos_vengan_con_otro_desfase()
    {
        var offset = TimeSpan.FromHours(-4);
        var block = TimeRange.Create(
            new DateTimeOffset(2027, 1, 4, 8, 0, 0, offset),
            new DateTimeOffset(2027, 1, 4, 10, 0, 0, offset)).Value;

        var result = Calculate([block], []);

        result.Count.ShouldBe(1);
        result[0].ShouldBe(At(12));
        result[0].Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_cero_o_negativa_lanza_ArgumentOutOfRangeException(int duration)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Calculate([Range(8, 16)], [], duration: duration));
    }

    [Fact]
    public void Un_bloque_nulo_lanza_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            FreeSlotCalculator.Calculate(null!, [], WideWindow(), TwoHours));
    }

    [Fact]
    public void Una_coleccion_de_citas_nula_lanza_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            FreeSlotCalculator.Calculate([Range(8, 16)], null!, WideWindow(), TwoHours));
    }

    [Fact]
    public void Una_ventana_nula_lanza_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            FreeSlotCalculator.Calculate([Range(8, 16)], [], null!, TwoHours));
    }
}
