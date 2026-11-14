using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.UpdatePatientProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Patients;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public class UpdatePatientProfileCommandValidatorTests
{
    private readonly UpdatePatientProfileCommandValidator _validator = new(new FakeDateTimeProvider());

    [Fact]
    public void Validate_admite_solo_el_telefono()
    {
        _validator.TestValidate(UpdatePatientProfileCommandMother.WithRequiredData()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_admite_todos_los_datos()
    {
        _validator.TestValidate(UpdatePatientProfileCommandMother.WithAllData(insuranceProviderId: 1))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_admite_las_longitudes_maximas()
    {
        var command = UpdatePatientProfileCommandMother.WithAllData(insuranceProviderId: 1) with
        {
            PhoneNumber = new string('1', 20),
            Address = new string('A', 250),
            EmergencyContactName = new string('A', 150),
            EmergencyContactPhone = new string('1', 20),
            PolicyNumber = new string('A', 50)
        };

        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_sin_telefono_utiliza_el_error_del_dominio(string phoneNumber)
    {
        var command = UpdatePatientProfileCommandMother.WithRequiredData() with { PhoneNumber = phoneNumber };

        AssertSingleError(command, nameof(UpdatePatientProfileCommand.PhoneNumber), PatientErrors.PhoneRequired);
    }

    [Fact]
    public void Validate_rechaza_telefono_de_21_caracteres()
    {
        var command = UpdatePatientProfileCommandMother.WithRequiredData() with { PhoneNumber = new string('1', 21) };

        AssertSingleError(command, nameof(UpdatePatientProfileCommand.PhoneNumber), PatientErrors.PhoneTooLong);
    }

    [Fact]
    public void Validate_rechaza_direccion_de_251_caracteres()
    {
        var command = UpdatePatientProfileCommandMother.WithRequiredData() with { Address = new string('A', 251) };

        AssertSingleError(command, nameof(UpdatePatientProfileCommand.Address), PatientErrors.AddressTooLong);
    }

    [Theory]
    [InlineData(UpdatePatientProfileCommandMother.NewEmergencyContactName, null)]
    [InlineData(null, UpdatePatientProfileCommandMother.NewEmergencyContactPhone)]
    public void Validate_rechaza_contacto_de_emergencia_incompleto(string? name, string? phone)
    {
        var command = UpdatePatientProfileCommandMother.WithRequiredData() with
        {
            EmergencyContactName = name,
            EmergencyContactPhone = phone
        };

        AssertSingleError(command, nameof(UpdatePatientProfileCommand.EmergencyContactName),
            PatientErrors.IncompleteEmergencyContact);
    }

    [Fact]
    public void Validate_rechaza_proveedor_sin_numero_de_poliza()
    {
        var command = UpdatePatientProfileCommandMother.WithAllData(insuranceProviderId: 1) with { PolicyNumber = null };

        AssertSingleError(command, nameof(UpdatePatientProfileCommand.PolicyNumber), PatientErrors.PolicyNumberRequired);
    }

    [Fact]
    public void Validate_rechaza_poliza_de_51_caracteres()
    {
        var command = UpdatePatientProfileCommandMother.WithAllData(insuranceProviderId: 1) with
        {
            PolicyNumber = new string('A', 51)
        };

        AssertSingleError(command, nameof(UpdatePatientProfileCommand.PolicyNumber), PatientErrors.PolicyNumberTooLong);
    }

    [Fact]
    public void Validate_rechaza_id_de_proveedor_no_positivo()
    {
        var command = UpdatePatientProfileCommandMother.WithAllData(insuranceProviderId: 0);

        AssertSingleError(command, nameof(UpdatePatientProfileCommand.InsuranceProviderId),
            PatientErrors.InvalidInsuranceProvider);
    }

    private void AssertSingleError(UpdatePatientProfileCommand command, string propertyName, Error error)
    {
        var failure = _validator.TestValidate(command).Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(propertyName);
        failure.ErrorCode.ShouldBe(error.Code);
        failure.ErrorMessage.ShouldBe(error.Description);
    }
}
