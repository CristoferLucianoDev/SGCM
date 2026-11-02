using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.ActivateSpecialty;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class ActivateSpecialtyCommandValidatorTests
{
    private readonly ActivateSpecialtyCommandValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void Validate_acepta_un_id_positivo(int id)
    {
        _validator.TestValidate(new ActivateSpecialtyCommand(id)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_rechaza_un_id_no_positivo_con_el_codigo_y_mensaje_existentes(int id)
    {
        var result = _validator.TestValidate(new ActivateSpecialtyCommand(id));

        result.ShouldHaveValidationErrorFor(command => command.Id)
            .WithErrorCode(SpecialtyErrors.InvalidId.Code)
            .WithErrorMessage(SpecialtyErrors.InvalidId.Description);
        result.Errors.ShouldHaveSingleItem();
    }
}
