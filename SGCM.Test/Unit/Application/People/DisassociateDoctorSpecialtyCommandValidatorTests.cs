using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class DisassociateDoctorSpecialtyCommandValidatorTests
{
    private readonly DisassociateDoctorSpecialtyCommandValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void Validate_acepta_ids_positivos(int id) =>
        _validator.TestValidate(new DisassociateDoctorSpecialtyCommand(id)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_rechaza_ids_no_positivos_con_el_error_de_Specialty(int id)
    {
        var validation = _validator.TestValidate(new DisassociateDoctorSpecialtyCommand(id));

        validation.ShouldHaveValidationErrorFor(command => command.SpecialtyId)
            .WithErrorCode(SpecialtyErrors.InvalidId.Code)
            .WithErrorMessage(SpecialtyErrors.InvalidId.Description);
        validation.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("Specialty.InvalidId");
    }
}
