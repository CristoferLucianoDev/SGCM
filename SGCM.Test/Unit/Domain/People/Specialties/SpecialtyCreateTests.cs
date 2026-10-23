using System.Globalization;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Specialties;

public class SpecialtyCreateTests
{
    [Fact]
    public void Create_con_datos_validos_devuelve_una_especialidad_activa()
    {
        var result = Specialty.Create("Cardiología", "Atención del corazón.");

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        result.Value.Name.ShouldBe("Cardiología");
        result.Value.Description.ShouldBe("Atención del corazón.");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("cardiología pediátrica", "Cardiología Pediátrica")]
    [InlineData("CARDIOLOGÍA PEDIÁTRICA", "Cardiología Pediátrica")]
    [InlineData("cArDiOlOgÍa pEdIáTrIcA", "Cardiología Pediátrica")]
    [InlineData("Cardiología Pediátrica", "Cardiología Pediátrica")]
    [InlineData("ATENCIÓN A LA NIÑEZ", "Atención A La Niñez")]
    [InlineData("ÑANDÚ", "Ñandú")]
    [InlineData("medicina  interna", "Medicina  Interna")]
    [InlineData("medicina\tinterna", "Medicina\tInterna")]
    public void Create_guarda_el_nombre_en_formato_titulo(string name, string expected)
    {
        var result = Specialty.Create(name, "Descripción válida.");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe(expected);
    }

    [Fact]
    public void Create_recorta_los_extremos_y_conserva_el_contenido_interno_de_la_descripcion()
    {
        var result = Specialty.Create(" \tCARDIOLOGÍA  PEDIÁTRICA\r\n", " \tAtención  MÉDICA\npara la niñez. \r\n");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Cardiología  Pediátrica");
        result.Value.Description.ShouldBe("Atención  MÉDICA\npara la niñez.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(" \r\n\t ")]
    public void Create_sin_nombre_devuelve_error(string? name)
    {
        var result = Specialty.Create(name, "Descripción válida.");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.NameRequired);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(" \r\n\t ")]
    public void Create_sin_descripcion_devuelve_error(string? description)
    {
        var result = Specialty.Create("Cardiología", description);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.DescriptionRequired);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Create_con_nombre_dentro_del_limite_es_valido(int length)
    {
        var result = Specialty.Create(new string('a', length), "Descripción válida.");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("A" + new string('a', length - 1));
    }

    [Fact]
    public void Create_con_nombre_de_101_caracteres_devuelve_error()
    {
        var result = Specialty.Create(new string('a', 101), "Descripción válida.");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.NameTooLong);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void Create_con_descripcion_dentro_del_limite_es_valido(int length)
    {
        var description = new string('a', length);

        var result = Specialty.Create("Cardiología", description);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Description.ShouldBe(description);
    }

    [Fact]
    public void Create_con_descripcion_de_501_caracteres_devuelve_error()
    {
        var result = Specialty.Create("Cardiología", new string('a', 501));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.DescriptionTooLong);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_aplica_los_limites_despues_de_recortar_los_extremos()
    {
        var result = Specialty.Create("  " + new string('a', 100) + "  ", "  " + new string('b', 500) + "  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("A" + new string('a', 99));
        result.Value.Description.ShouldBe(new string('b', 500));
    }

    [Fact]
    public void Create_no_depende_de_la_cultura_actual()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            var result = Specialty.Create("MEDICINA INTERNA", "Descripción válida.");

            result.IsSuccess.ShouldBeTrue();
            result.Value.Name.ShouldBe("Medicina Interna");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Activate_en_una_especialidad_nueva_usa_el_nombre_del_agregado()
    {
        var specialty = Specialty.Create("Cardiología", "Descripción válida.").Value;

        var result = specialty.Activate();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ActivationErrors.AlreadyActive("Specialty"));
        specialty.IsActive.ShouldBeTrue();
    }
}
