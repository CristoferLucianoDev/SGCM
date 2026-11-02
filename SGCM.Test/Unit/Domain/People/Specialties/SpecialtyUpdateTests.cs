using System.Globalization;
using System.Reflection;
using NSubstitute;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Specialties;

public sealed class SpecialtyUpdateTests
{
    private const int OriginalId = 42;
    private const string OriginalName = "Cardiología";
    private const string OriginalDescription = "Atención  del corazón.\nPara la niñez.";

    public static IEnumerable<object[]> SelectedFields()
    {
        foreach (var active in new[] { true, false })
        {
            yield return [true, false, active];
            yield return [false, true, active];
            yield return [true, true, active];
        }
    }

    [Theory]
    [MemberData(nameof(SelectedFields))]
    public void Update_modifica_solo_los_campos_seleccionados_y_conserva_identidad_y_estado(
        bool nameProvided, bool descriptionProvided, bool active)
    {
        var specialty = CreateSpecialty(active);

        var result = specialty.Update(nameProvided, " \tMEDICINA INTERNA\r\n",
            descriptionProvided, " \tAtención  MÉDICA\npara la niñez. \r\n");

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        specialty.Name.ShouldBe(nameProvided ? "Medicina Interna" : OriginalName);
        specialty.Description.ShouldBe(descriptionProvided ? "Atención  MÉDICA\npara la niñez." : OriginalDescription);
        AssertIdentityAndState(specialty, active);
    }

    public static IEnumerable<object?[]> InvalidUpdates()
    {
        string?[] missing = [null, "", "   ", "\t", " \r\n\t "];
        foreach (var active in new[] { true, false })
        {
            foreach (var value in missing)
            {
                yield return [value, "Descripción nueva.", "Specialty.NameRequired", active];
                yield return ["Medicina Interna", value, "Specialty.DescriptionRequired", active];
            }
            yield return [new string('a', 101), "Descripción nueva.", "Specialty.NameTooLong", active];
            yield return ["Medicina Interna", new string('b', 501), "Specialty.DescriptionTooLong", active];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public void Update_invalido_devuelve_el_mismo_error_que_Create_sin_mutar_ningun_campo(
        string? name, string? description, string expectedCode, bool active)
    {
        var specialty = CreateSpecialty(active);
        var creation = Specialty.Create(name, description);

        var result = specialty.Update(true, name, true, description);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
        creation.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(creation.Error);
        AssertUnchanged(specialty, active);
    }

    public static IEnumerable<object?[]> ErrorPriorityCases()
    {
        yield return [null, null, "Specialty.NameRequired"];
        yield return [new string('a', 101), "", "Specialty.NameTooLong"];
        yield return [new string('a', 101), new string('b', 501), "Specialty.NameTooLong"];
    }

    [Theory]
    [MemberData(nameof(ErrorPriorityCases))]
    public void Update_con_ambos_campos_invalidos_respeta_la_prioridad_de_Create(
        string? name, string? description, string expectedCode)
    {
        var specialty = CreateSpecialty();

        var result = specialty.Update(true, name, true, description);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(expectedCode);
        result.Error.ShouldBe(Specialty.Create(name, description).Error);
        AssertUnchanged(specialty, true);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 500)]
    public void Update_acepta_los_limites_y_coincide_con_la_normalizacion_de_Create(int nameLength, int descriptionLength)
    {
        var specialty = CreateSpecialty();
        var name = new string('a', nameLength);
        var description = new string('b', descriptionLength);
        var creation = Specialty.Create(name, description);

        var result = specialty.Update(true, name, true, description);

        result.IsSuccess.ShouldBeTrue();
        creation.IsSuccess.ShouldBeTrue();
        specialty.Name.ShouldBe("A" + new string('a', nameLength - 1));
        specialty.Description.ShouldBe(description);
        specialty.Name.ShouldBe(creation.Value.Name);
        specialty.Description.ShouldBe(creation.Value.Description);
        AssertIdentityAndState(specialty, true);
    }

    [Fact]
    public void Update_comprueba_los_limites_despues_de_recortar_y_normalizar()
    {
        var specialty = CreateSpecialty();

        var result = specialty.Update(true, " \t" + new string('A', 100) + "\r\n ",
            true, " \t" + new string('b', 500) + "\r\n ");

        result.IsSuccess.ShouldBeTrue();
        specialty.Name.ShouldBe("A" + new string('a', 99));
        specialty.Description.ShouldBe(new string('b', 500));
        AssertIdentityAndState(specialty, true);
    }

    [Theory]
    [InlineData("cardiología pediátrica", "Cardiología Pediátrica")]
    [InlineData("CARDIOLOGÍA PEDIÁTRICA", "Cardiología Pediátrica")]
    [InlineData("cArDiOlOgÍa pEdIáTrIcA", "Cardiología Pediátrica")]
    [InlineData("ATENCIÓN A LA NIÑEZ", "Atención A La Niñez")]
    [InlineData("ÑANDÚ", "Ñandú")]
    [InlineData("medicina  interna", "Medicina  Interna")]
    [InlineData("medicina\tinterna", "Medicina\tInterna")]
    public void Update_normaliza_el_nombre_y_conserva_tildes_y_separadores_internos(string name, string expected)
    {
        var specialty = CreateSpecialty();

        var result = specialty.Update(true, name, false, null);

        result.IsSuccess.ShouldBeTrue();
        specialty.Name.ShouldBe(expected);
        specialty.Description.ShouldBe(OriginalDescription);
        AssertIdentityAndState(specialty, true);
    }

    [Fact]
    public void Update_no_depende_de_la_cultura_del_proceso()
    {
        var specialty = CreateSpecialty();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            var result = specialty.Update(true, "MEDICINA INTERNA", true, "Descripción VÁLIDA.");

            result.IsSuccess.ShouldBeTrue();
            specialty.Name.ShouldBe("Medicina Interna");
            specialty.Description.ShouldBe("Descripción VÁLIDA.");
            AssertIdentityAndState(specialty, true);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Update_ignora_el_valor_invalido_de_un_campo_no_seleccionado(bool nameProvided)
    {
        var specialty = CreateSpecialty();

        var result = specialty.Update(nameProvided, nameProvided ? "Medicina Interna" : new string('a', 101),
            !nameProvided, nameProvided ? new string('b', 501) : "Descripción nueva.");

        result.IsSuccess.ShouldBeTrue();
        specialty.Name.ShouldBe(nameProvided ? "Medicina Interna" : OriginalName);
        specialty.Description.ShouldBe(nameProvided ? OriginalDescription : "Descripción nueva.");
        AssertIdentityAndState(specialty, true);
    }

    [Theory]
    [InlineData(true, null, null)]
    [InlineData(false, null, null)]
    [InlineData(true, "IGNORADO", "IGNORADA")]
    [InlineData(false, "IGNORADO", "IGNORADA")]
    public void Update_sin_campos_seleccionados_devuelve_NoFieldsProvided_sin_cambios(
        bool active, string? name, string? description)
    {
        var specialty = CreateSpecialty(active);

        var result = specialty.Update(false, name, false, description);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.NoFieldsProvided);
        result.Error.Code.ShouldBe("Specialty.NoFieldsProvided");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Description.ShouldBe("Debe proporcionar el nombre, la descripción o ambos para actualizar la especialidad.");
        AssertUnchanged(specialty, active);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Update_acepta_valores_equivalentes_a_los_actuales(bool active)
    {
        var specialty = CreateSpecialty(active);

        var result = specialty.Update(true, "  CARDIOLOGÍA  ", true, " \t" + OriginalDescription + "\r\n ");

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        AssertUnchanged(specialty, active);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Update_conserva_exactamente_el_campo_omitido_aunque_no_este_normalizado(bool nameProvided)
    {
        var specialty = CreateSpecialty();
        var omittedValue = nameProvided ? " \tAtención  MÉDICA.\n " : " \tCARDIOLOGÍA  PEDIÁTRICA ";
        var omittedProperty = nameProvided ? nameof(Specialty.Description) : nameof(Specialty.Name);
        typeof(Specialty).GetProperty(omittedProperty)!.SetValue(specialty, omittedValue);

        var result = specialty.Update(nameProvided, "Medicina Interna", !nameProvided, "Descripción nueva.");

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        specialty.Name.ShouldBe(nameProvided ? "Medicina Interna" : omittedValue);
        specialty.Description.ShouldBe(nameProvided ? omittedValue : "Descripción nueva.");
        AssertIdentityAndState(specialty, true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Update_conserva_los_eventos_pendientes_tanto_si_tiene_exito_como_si_falla(bool valid)
    {
        var specialty = CreateSpecialty(false);
        var pendingEvent = Substitute.For<IDomainEvent>();
        typeof(AggregateRoot).GetMethod("Raise", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(specialty, [pendingEvent]);

        var result = specialty.Update(true, "Medicina Interna", true, valid ? "Descripción nueva." : null);

        result.IsSuccess.ShouldBe(valid);
        result.Error.ShouldBe(valid ? Error.None : SpecialtyErrors.DescriptionRequired);
        specialty.Name.ShouldBe(valid ? "Medicina Interna" : OriginalName);
        specialty.Description.ShouldBe(valid ? "Descripción nueva." : OriginalDescription);
        specialty.Id.ShouldBe(OriginalId);
        specialty.IsActive.ShouldBeFalse();
        specialty.DomainEvents.Count.ShouldBe(1);
        specialty.DomainEvents.Single().ShouldBeSameAs(pendingEvent);
    }

    private static Specialty CreateSpecialty(bool active = true)
    {
        var specialty = Specialty.Create(OriginalName, OriginalDescription).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(specialty, OriginalId);
        if (!active)
        {
            specialty.Deactivate().IsSuccess.ShouldBeTrue();
        }
        return specialty;
    }

    private static void AssertUnchanged(Specialty specialty, bool active)
    {
        specialty.Name.ShouldBe(OriginalName);
        specialty.Description.ShouldBe(OriginalDescription);
        AssertIdentityAndState(specialty, active);
    }

    private static void AssertIdentityAndState(Specialty specialty, bool active)
    {
        specialty.Id.ShouldBe(OriginalId);
        specialty.IsActive.ShouldBe(active);
        specialty.DomainEvents.ShouldBeEmpty();
    }
}
