using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.UpdateSpecialty;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class UpdateSpecialtyCommandValidatorTests
{
    private readonly UpdateSpecialtyCommandValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_rechaza_el_id_invalido_antes_de_los_campos(int id)
    {
        var result = _validator.TestValidate(new UpdateSpecialtyCommand(id, false, null, false, null));

        result.ShouldHaveValidationErrorFor(command => command.Id).WithErrorCode(SpecialtyErrors.InvalidId.Code);
        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(SpecialtyErrors.InvalidId.Description);
    }

    public static IEnumerable<object?[]> InvalidFields()
    {
        foreach (var value in new string?[] { null, "", " \t\r\n " })
        {
            yield return [true, value, true, "Descripción válida.", "Name", "Specialty.NameRequired"];
            yield return [true, "Neurología", true, value, "Description", "Specialty.DescriptionRequired"];
        }
        yield return [true, new string('a', 101), false, null, "Name", "Specialty.NameTooLong"];
        yield return [false, null, true, new string('b', 501), "Description", "Specialty.DescriptionTooLong"];
        yield return [false, null, false, null, "", "Specialty.NoFieldsProvided"];
        yield return [false, "Ignorado", false, "Ignorada", "", "Specialty.NoFieldsProvided"];
        yield return [true, null, true, null, "Name", "Specialty.NameRequired"];
        yield return [true, new string('a', 101), true, "", "Name", "Specialty.NameTooLong"];
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void Validate_reutiliza_el_error_y_la_prioridad_del_dominio(
        bool nameProvided, string? name, bool descriptionProvided, string? description, string property, string code)
    {
        var expected = Specialty.Create("Cardiología", "Descripción válida.").Value
            .Update(nameProvided, name, descriptionProvided, description);

        var result = _validator.TestValidate(new UpdateSpecialtyCommand(42, nameProvided, name, descriptionProvided, description));

        expected.IsFailure.ShouldBeTrue();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(property);
        failure.ErrorCode.ShouldBe(code);
        failure.ErrorMessage.ShouldBe(expected.Error.Description);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Validate_admite_los_campos_seleccionados_y_los_limites_despues_del_recorte(bool nameProvided, bool descriptionProvided)
    {
        var command = new UpdateSpecialtyCommand(int.MaxValue, nameProvided, " \t" + new string('A', 100) + "  ",
            descriptionProvided, "  " + new string('b', 500) + " \r\n");

        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_ignora_el_contenido_invalido_del_campo_omitido(bool nameProvided)
    {
        var command = new UpdateSpecialtyCommand(42, nameProvided, nameProvided ? "ATENCIÓN A LA NIÑEZ" : new string('a', 101),
            !nameProvided, nameProvided ? new string('b', 501) : "Descripción válida.");

        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }
}
