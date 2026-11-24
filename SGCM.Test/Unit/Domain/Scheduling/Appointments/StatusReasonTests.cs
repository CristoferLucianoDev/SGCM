using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Appointments;

public sealed class StatusReasonTests
{
    private const int MinimumWords = 30;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_motivo_vacio_es_obligatorio_en_Create_y_nulo_en_CreateOptional(string? value)
    {
        var required = StatusReason.Create(value);
        var optional = StatusReason.CreateOptional(value);

        required.IsFailure.ShouldBeTrue();
        required.Error.ShouldBe(AppointmentErrors.StatusReasonRequired);
        optional.IsSuccess.ShouldBeTrue();
        optional.Value.ShouldBeNull();
    }

    [Fact]
    public void El_motivo_se_guarda_recortado()
    {
        StatusReason.Create("  Tengo un viaje  ").Value.Value.ShouldBe("Tengo un viaje");
        StatusReason.CreateOptional("  Tengo un viaje  ").Value!.Value.ShouldBe("Tengo un viaje");
    }

    [Fact]
    public void Un_motivo_de_la_longitud_maxima_se_acepta()
    {
        var value = new string('a', StatusReason.MaxLength);

        StatusReason.Create(value).IsSuccess.ShouldBeTrue();
        StatusReason.CreateOptional(value).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Un_motivo_demasiado_largo_se_rechaza_en_ambos_metodos()
    {
        var value = new string('a', StatusReason.MaxLength + 1);

        StatusReason.Create(value).Error.ShouldBe(AppointmentErrors.StatusReasonTooLong);
        StatusReason.CreateOptional(value).Error.ShouldBe(AppointmentErrors.StatusReasonTooLong);
        StatusReason.Create(value).Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void La_longitud_se_mide_recortada()
    {
        var value = $"  {new string('a', StatusReason.MaxLength)}  ";

        StatusReason.Create(value).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("una", 1)]
    [InlineData("  dos    palabras  ", 2)]
    [InlineData("con\ttabs\ty\tmas", 4)]
    [InlineData("con\nsaltos\r\nde linea", 4)]
    public void Las_palabras_se_separan_por_cualquier_espacio_en_blanco(string value, int expected)
    {
        StatusReason.Create(value).Value.WordCount.ShouldBe(expected);
    }

    [Fact]
    public void El_minimo_de_palabras_se_comprueba_en_el_borde()
    {
        StatusReason.Create(Words(MinimumWords - 1)).Value.HasAtLeastWords(MinimumWords).ShouldBeFalse();
        StatusReason.Create(Words(MinimumWords)).Value.HasAtLeastWords(MinimumWords).ShouldBeTrue();
    }

    private static string Words(int count) => string.Join(" ", Enumerable.Repeat("palabra", count));
}
