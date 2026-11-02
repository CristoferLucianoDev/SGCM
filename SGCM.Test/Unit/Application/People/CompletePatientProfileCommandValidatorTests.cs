using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.CompletePatientProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Patients;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public class CompletePatientProfileCommandValidatorTests
{
    private readonly FakeDateTimeProvider _clock = new();
    private readonly CompletePatientProfileCommandValidator _validator;

    public CompletePatientProfileCommandValidatorTests()
    {
        _validator = new CompletePatientProfileCommandValidator(_clock);
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    [Fact]
    public void Validate_admite_solo_los_datos_obligatorios()
    {
        _validator.TestValidate(CompletePatientProfileCommandMother.WithRequiredData()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_admite_todos_los_datos()
    {
        _validator.TestValidate(CompletePatientProfileCommandMother.WithAllData(insuranceProviderId: 1))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_admite_las_longitudes_maximas()
    {
        var command = CompletePatientProfileCommandMother.WithAllData(insuranceProviderId: 1) with
        {
            FirstName = new string('A', 100),
            LastName = new string('A', 100),
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
    public void Validate_sin_nombre_utiliza_el_error_del_dominio(string firstName)
    {
        var result = Validate(CompletePatientProfileCommandMother.WithRequiredData() with { FirstName = firstName });

        AssertSingleError(result, nameof(CompletePatientProfileCommand.FirstName), PatientErrors.FirstNameRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_sin_apellido_utiliza_el_error_del_dominio(string lastName)
    {
        var result = Validate(CompletePatientProfileCommandMother.WithRequiredData() with { LastName = lastName });

        AssertSingleError(result, nameof(CompletePatientProfileCommand.LastName), PatientErrors.LastNameRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_sin_telefono_utiliza_el_error_del_dominio(string phoneNumber)
    {
        var result = Validate(CompletePatientProfileCommandMother.WithRequiredData() with { PhoneNumber = phoneNumber });

        AssertSingleError(result, nameof(CompletePatientProfileCommand.PhoneNumber), PatientErrors.PhoneRequired);
    }

    [Fact]
    public void Validate_rechaza_nombre_de_101_caracteres()
    {
        var result = Validate(CompletePatientProfileCommandMother.WithRequiredData() with { FirstName = new string('A', 101) });

        AssertSingleError(result, nameof(CompletePatientProfileCommand.FirstName), PatientErrors.FirstNameTooLong);
    }

    [Fact]
    public void Validate_rechaza_apellido_de_101_caracteres()
    {
        var result = Validate(CompletePatientProfileCommandMother.WithRequiredData() with { LastName = new string('A', 101) });

        AssertSingleError(result, nameof(CompletePatientProfileCommand.LastName), PatientErrors.LastNameTooLong);
    }

    [Fact]
    public void Validate_rechaza_telefono_de_21_caracteres()
    {
        var result = Validate(CompletePatientProfileCommandMother.WithRequiredData() with { PhoneNumber = new string('1', 21) });

        AssertSingleError(result, nameof(CompletePatientProfileCommand.PhoneNumber), PatientErrors.PhoneTooLong);
    }

    [Fact]
    public void Validate_rechaza_direccion_de_251_caracteres()
    {
        var result = Validate(CompletePatientProfileCommandMother.WithRequiredData() with { Address = new string('A', 251) });

        AssertSingleError(result, nameof(CompletePatientProfileCommand.Address), PatientErrors.AddressTooLong);
    }

    [Fact]
    public void Validate_rechaza_poliza_de_51_caracteres()
    {
        var command = CompletePatientProfileCommandMother.WithAllData(insuranceProviderId: 1) with { PolicyNumber = new string('A', 51) };

        AssertSingleError(Validate(command), nameof(CompletePatientProfileCommand.PolicyNumber), PatientErrors.PolicyNumberTooLong);
    }

    [Fact]
    public void Validate_admite_nacer_hoy()
    {
        var command = CompletePatientProfileCommandMother.WithRequiredData() with { BirthDate = Today };

        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_rechaza_fecha_de_nacimiento_futura()
    {
        var command = CompletePatientProfileCommandMother.WithRequiredData() with { BirthDate = Today.AddDays(1) };

        AssertSingleError(Validate(command), nameof(CompletePatientProfileCommand.BirthDate), PatientErrors.InvalidBirthDate);
    }

    [Fact]
    public void Validate_rechaza_fecha_de_nacimiento_ausente()
    {
        var command = CompletePatientProfileCommandMother.WithRequiredData() with { BirthDate = default };

        AssertSingleError(Validate(command), nameof(CompletePatientProfileCommand.BirthDate), PatientErrors.InvalidBirthDate);
    }

    [Theory]
    [InlineData(PatientMother.DefaultEmergencyContactName, null)]
    [InlineData(null, PatientMother.DefaultEmergencyContactPhone)]
    public void Validate_rechaza_contacto_de_emergencia_incompleto(string? name, string? phone)
    {
        var command = CompletePatientProfileCommandMother.WithRequiredData() with
        {
            EmergencyContactName = name,
            EmergencyContactPhone = phone
        };

        AssertSingleError(Validate(command), nameof(CompletePatientProfileCommand.EmergencyContactName),
            PatientErrors.IncompleteEmergencyContact);
    }

    [Fact]
    public void Validate_rechaza_proveedor_sin_numero_de_poliza()
    {
        var command = CompletePatientProfileCommandMother.WithAllData(insuranceProviderId: 1) with { PolicyNumber = null };

        AssertSingleError(Validate(command), nameof(CompletePatientProfileCommand.PolicyNumber), PatientErrors.PolicyNumberRequired);
    }

    [Fact]
    public void Validate_rechaza_id_de_proveedor_no_positivo()
    {
        var command = CompletePatientProfileCommandMother.WithAllData(insuranceProviderId: 0);

        AssertSingleError(Validate(command), nameof(CompletePatientProfileCommand.InsuranceProviderId),
            PatientErrors.InvalidInsuranceProvider);
    }

    [Fact]
    public void Validate_conserva_la_prioridad_del_primer_error_del_dominio()
    {
        var command = CompletePatientProfileCommandMother.WithRequiredData() with { FirstName = "", PhoneNumber = "" };

        Validate(command).Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(PatientErrors.FirstNameRequired.Code);
    }

    private TestValidationResult<CompletePatientProfileCommand> Validate(CompletePatientProfileCommand command) =>
        _validator.TestValidate(command);

    private static void AssertSingleError(
        TestValidationResult<CompletePatientProfileCommand> result, string propertyName, Error error)
    {
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(propertyName);
        failure.ErrorCode.ShouldBe(error.Code);
        failure.ErrorMessage.ShouldBe(error.Description);
    }
}
