using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public class CreateSpecialtyCommandValidatorTests
{
    private readonly CreateSpecialtyCommandValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Validate_sin_nombre_utiliza_el_error_del_dominio(string? name)
    {
        var result = _validator.TestValidate(new CreateSpecialtyCommand(name!, "Descripción válida."));

        result.ShouldHaveValidationErrorFor(command => command.Name).WithErrorCode(SpecialtyErrors.NameRequired.Code);
        result.Errors.Single().ErrorMessage.ShouldBe(SpecialtyErrors.NameRequired.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Validate_sin_descripcion_utiliza_el_error_del_dominio(string? description)
    {
        var result = _validator.TestValidate(new CreateSpecialtyCommand("Cardiología", description!));

        result.ShouldHaveValidationErrorFor(command => command.Description).WithErrorCode(SpecialtyErrors.DescriptionRequired.Code);
        result.Errors.Single().ErrorMessage.ShouldBe(SpecialtyErrors.DescriptionRequired.Description);
    }

    [Fact]
    public void Validate_admite_los_limites_despues_del_recorte()
    {
        var result = _validator.TestValidate(new CreateSpecialtyCommand(
            "  " + new string('a', 100) + "  ", "  " + new string('b', 500) + "  "));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_rechaza_nombre_de_101_caracteres()
    {
        var result = _validator.TestValidate(new CreateSpecialtyCommand(new string('a', 101), "Descripción válida."));

        result.ShouldHaveValidationErrorFor(command => command.Name).WithErrorCode(SpecialtyErrors.NameTooLong.Code);
    }

    [Fact]
    public void Validate_rechaza_descripcion_de_501_caracteres()
    {
        var result = _validator.TestValidate(new CreateSpecialtyCommand("Cardiología", new string('a', 501)));

        result.ShouldHaveValidationErrorFor(command => command.Description).WithErrorCode(SpecialtyErrors.DescriptionTooLong.Code);
    }

    [Fact]
    public void Validate_conserva_la_prioridad_del_primer_error_del_dominio()
    {
        var result = _validator.TestValidate(new CreateSpecialtyCommand("", ""));

        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(SpecialtyErrors.NameRequired.Code);
    }

    [Fact]
    public void Validate_admite_nombre_con_tildes_y_en_mayusculas()
    {
        var result = _validator.TestValidate(new CreateSpecialtyCommand("  ATENCIÓN A LA NIÑEZ  ", "Descripción válida."));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
