using SGCM.Data.Application.People.Commands.CompleteDoctorProfile;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class CompleteDoctorProfileCommandValidatorTests
{
    private readonly CompleteDoctorProfileCommandValidator _validator = new();

    [Fact]
    public void Acepta_cero_y_los_limites_del_dominio()
    {
        var command = ValidCommand() with { ConsultationFee = 0m, YearsOfExperience = 0 };
        _validator.Validate(command).IsValid.ShouldBeTrue();
        _validator.Validate(command with
        {
            FirstName = new string('a', 100), LastName = new string('b', 100),
            LicenseNumber = new string('l', 50), PhoneNumber = new string('1', 20),
            AddressLine = new string('d', 250), Biography = new string('b', 1000),
            ConsultationFee = 99999999.990m, YearsOfExperience = int.MaxValue
        }).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("FirstName", "Doctor.FirstNameRequired", "ausente")]
    [InlineData("FirstName", "Doctor.FirstNameTooLong", "largo")]
    [InlineData("LastName", "Doctor.LastNameRequired", "ausente")]
    [InlineData("LastName", "Doctor.LastNameTooLong", "largo")]
    [InlineData("LicenseNumber", "Doctor.LicenseNumberRequired", "ausente")]
    [InlineData("LicenseNumber", "Doctor.LicenseNumberTooLong", "largo")]
    [InlineData("PhoneNumber", "Doctor.PhoneNumberRequired", "ausente")]
    [InlineData("PhoneNumber", "Doctor.PhoneNumberTooLong", "largo")]
    [InlineData("AddressLine", "Doctor.AddressLineRequired", "ausente")]
    [InlineData("AddressLine", "Doctor.AddressLineTooLong", "largo")]
    [InlineData("Biography", "Doctor.BiographyRequired", "ausente")]
    [InlineData("Biography", "Doctor.BiographyTooLong", "largo")]
    [InlineData("ProvinceId", "Doctor.InvalidProvinceId", "cero")]
    [InlineData("MunicipalityId", "Doctor.InvalidMunicipalityId", "negativo")]
    [InlineData("ConsultationFee", "Doctor.InvalidConsultationFee", "negativo")]
    [InlineData("ConsultationFee", "Doctor.InvalidConsultationFee", "exceso")]
    [InlineData("ConsultationFee", "Doctor.InvalidConsultationFee", "fraccion")]
    [InlineData("YearsOfExperience", "Doctor.InvalidYearsOfExperience", "negativo")]
    [InlineData("SpecialtyIds", "Doctor.SpecialtyRequired", "nulo")]
    [InlineData("SpecialtyIds", "Doctor.SpecialtyRequired", "vacio")]
    [InlineData("SpecialtyIds", "Doctor.SpecialtyAlreadyAssociated", "duplicado")]
    [InlineData("SpecialtyIds", "Specialty.InvalidId", "negativo")]
    public void Conserva_la_regla_del_dominio_y_el_campo(string property, string errorCode, string scenario)
    {
        var valid = ValidCommand();
        var invalid = (property, scenario) switch
        {
            ("FirstName", "ausente") => valid with { FirstName = null },
            ("FirstName", _) => valid with { FirstName = new string('a', 101) },
            ("LastName", "ausente") => valid with { LastName = " " },
            ("LastName", _) => valid with { LastName = new string('a', 101) },
            ("LicenseNumber", "ausente") => valid with { LicenseNumber = "" },
            ("LicenseNumber", _) => valid with { LicenseNumber = new string('a', 51) },
            ("PhoneNumber", "ausente") => valid with { PhoneNumber = " \t" },
            ("PhoneNumber", _) => valid with { PhoneNumber = new string('1', 21) },
            ("AddressLine", "ausente") => valid with { AddressLine = null },
            ("AddressLine", _) => valid with { AddressLine = new string('a', 251) },
            ("Biography", "ausente") => valid with { Biography = null },
            ("Biography", _) => valid with { Biography = new string('a', 1001) },
            ("ProvinceId", _) => valid with { ProvinceId = 0 },
            ("MunicipalityId", _) => valid with { MunicipalityId = -1 },
            ("ConsultationFee", "negativo") => valid with { ConsultationFee = -0.01m },
            ("ConsultationFee", "exceso") => valid with { ConsultationFee = 100000000m },
            ("ConsultationFee", _) => valid with { ConsultationFee = 12.345m },
            ("YearsOfExperience", _) => valid with { YearsOfExperience = -1 },
            ("SpecialtyIds", "nulo") => valid with { SpecialtyIds = null },
            ("SpecialtyIds", "vacio") => valid with { SpecialtyIds = [] },
            ("SpecialtyIds", "duplicado") => valid with { SpecialtyIds = [7, 7] },
            ("SpecialtyIds", "negativo") => valid with { SpecialtyIds = [7, -1] },
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };

        var failure = _validator.Validate(invalid).Errors.ShouldHaveSingleItem();

        failure.PropertyName.ShouldBe(property);
        failure.ErrorCode.ShouldBe(errorCode);
    }

    internal static CompleteDoctorProfileCommand ValidCommand() =>
        new(" Ana ", "Gómez", " LIC-252 ", " 809-555-0100 ", 4, 9, "Calle 252", 12.34m, 3,
            "Atención médica.", [7, 8], "Navegador", "192.0.2.5");
}
