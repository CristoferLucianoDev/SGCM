using SGCM.Data.Application.People.Commands.UpdateDoctorProfile;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class UpdateDoctorProfileCommandValidatorTests
{
    private readonly UpdateDoctorProfileCommandValidator _validator = new();

    internal static UpdateDoctorProfileCommand ValidCommand() =>
        new(" 809-555-0123 ", 4, 9, " Calle 255 ", 0m, 0, " Atención médica. ");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12.34, 8)]
    public void Acepta_ceros_y_otros_valores_validos(double fee, int experience) =>
        _validator.Validate(ValidCommand() with { ConsultationFee = (decimal)fee, YearsOfExperience = experience })
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(nameof(UpdateDoctorProfileCommand.PhoneNumber), "Doctor.PhoneNumberRequired")]
    [InlineData(nameof(UpdateDoctorProfileCommand.PhoneNumber), "Doctor.PhoneNumberTooLong")]
    [InlineData(nameof(UpdateDoctorProfileCommand.ProvinceId), "Doctor.InvalidProvinceId")]
    [InlineData(nameof(UpdateDoctorProfileCommand.MunicipalityId), "Doctor.InvalidMunicipalityId")]
    [InlineData(nameof(UpdateDoctorProfileCommand.AddressLine), "Doctor.AddressLineRequired")]
    [InlineData(nameof(UpdateDoctorProfileCommand.AddressLine), "Doctor.AddressLineTooLong")]
    [InlineData(nameof(UpdateDoctorProfileCommand.Biography), "Doctor.BiographyRequired")]
    [InlineData(nameof(UpdateDoctorProfileCommand.Biography), "Doctor.BiographyTooLong")]
    [InlineData(nameof(UpdateDoctorProfileCommand.ConsultationFee), "Doctor.InvalidConsultationFee", -0.01)]
    [InlineData(nameof(UpdateDoctorProfileCommand.ConsultationFee), "Doctor.InvalidConsultationFee", 100000000)]
    [InlineData(nameof(UpdateDoctorProfileCommand.ConsultationFee), "Doctor.InvalidConsultationFee", 0.001)]
    [InlineData(nameof(UpdateDoctorProfileCommand.YearsOfExperience), "Doctor.InvalidYearsOfExperience")]
    public void Conserva_el_campo_codigo_y_mensaje_del_dominio(string property, string code, double fee = 0)
    {
        var valid = ValidCommand();
        var command = code switch
        {
            "Doctor.PhoneNumberRequired" => valid with { PhoneNumber = " " },
            "Doctor.PhoneNumberTooLong" => valid with { PhoneNumber = new string('1', 21) },
            "Doctor.InvalidProvinceId" => valid with { ProvinceId = 0 },
            "Doctor.InvalidMunicipalityId" => valid with { MunicipalityId = -1 },
            "Doctor.AddressLineRequired" => valid with { AddressLine = "" },
            "Doctor.AddressLineTooLong" => valid with { AddressLine = new string('a', 251) },
            "Doctor.BiographyRequired" => valid with { Biography = null },
            "Doctor.BiographyTooLong" => valid with { Biography = new string('a', 1001) },
            "Doctor.InvalidConsultationFee" => valid with { ConsultationFee = (decimal)fee },
            "Doctor.InvalidYearsOfExperience" => valid with { YearsOfExperience = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
        var doctor = DoctorMother.Create([11]).Value;
        var domain = doctor.UpdateProfessionalData(command.PhoneNumber, command.ProvinceId, command.MunicipalityId,
            command.AddressLine, command.ConsultationFee, command.YearsOfExperience, command.Biography);

        var error = _validator.Validate(command).Errors.ShouldHaveSingleItem();

        domain.IsFailure.ShouldBeTrue();
        error.PropertyName.ShouldBe(property);
        error.ErrorCode.ShouldBe(code);
        error.ErrorCode.ShouldBe(domain.Error.Code);
        error.ErrorMessage.ShouldBe(domain.Error.Description);
    }
}
