using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.People.InsuranceProviders;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class RegisterInsuranceProviderCommandValidatorTests
{
    private const string Name = "Seguros Universal";
    private const string ContactPhone = "8095550200";
    private const string ContactEmail = "contacto@seguros.com";
    private const string Address = "Av. Winston Churchill 100";
    private const string Website = "https://www.segurosuniversal.com";
    private const string EmailDomain = "@correo.com";

    private readonly RegisterInsuranceProviderCommandValidator _validator = new();

    public static TheoryData<string?> MissingValues => new() { null, "", "   ", "\t\r\n" };

    public static TheoryData<string?> InvalidEmails => new()
    {
        null,
        "",
        "sin-arroba",
        new string('a', Email.MaxLength + 1 - EmailDomain.Length) + EmailDomain
    };

    [Theory]
    [MemberData(nameof(MissingValues))]
    public void Validate_sin_nombre_utiliza_el_error_del_dominio(string? name)
    {
        var result = _validator.TestValidate(Command(name: name));

        result.ShouldHaveValidationErrorFor(command => command.Name).WithErrorCode(InsuranceProviderErrors.NameRequired.Code);
        result.Errors.Single().ErrorMessage.ShouldBe(InsuranceProviderErrors.NameRequired.Description);
    }

    [Theory]
    [MemberData(nameof(MissingValues))]
    public void Validate_sin_telefono_utiliza_el_error_del_dominio(string? contactPhone)
    {
        var result = _validator.TestValidate(Command(contactPhone: contactPhone));

        result.ShouldHaveValidationErrorFor(command => command.ContactPhone)
            .WithErrorCode(InsuranceProviderErrors.ContactPhoneRequired.Code);
        result.Errors.Single().ErrorMessage.ShouldBe(InsuranceProviderErrors.ContactPhoneRequired.Description);
    }

    [Theory]
    [MemberData(nameof(MissingValues))]
    public void Validate_sin_direccion_utiliza_el_error_del_dominio(string? address)
    {
        var result = _validator.TestValidate(Command(address: address));

        result.ShouldHaveValidationErrorFor(command => command.Address).WithErrorCode(InsuranceProviderErrors.AddressRequired.Code);
        result.Errors.Single().ErrorMessage.ShouldBe(InsuranceProviderErrors.AddressRequired.Description);
    }

    [Fact]
    public void Validate_rechaza_un_nombre_que_supera_el_maximo_despues_del_recorte()
    {
        var result = _validator.TestValidate(Command(name: Padded(InsuranceProvider.MaxNameLength + 1)));

        result.ShouldHaveValidationErrorFor(command => command.Name).WithErrorCode(InsuranceProviderErrors.NameTooLong.Code);
    }

    [Fact]
    public void Validate_rechaza_un_telefono_que_supera_el_maximo_despues_del_recorte()
    {
        var result = _validator.TestValidate(Command(contactPhone: Padded(InsuranceProvider.MaxContactPhoneLength + 1)));

        result.ShouldHaveValidationErrorFor(command => command.ContactPhone)
            .WithErrorCode(InsuranceProviderErrors.ContactPhoneTooLong.Code);
    }

    [Fact]
    public void Validate_rechaza_una_direccion_que_supera_el_maximo_despues_del_recorte()
    {
        var result = _validator.TestValidate(Command(address: Padded(InsuranceProvider.MaxAddressLength + 1)));

        result.ShouldHaveValidationErrorFor(command => command.Address).WithErrorCode(InsuranceProviderErrors.AddressTooLong.Code);
    }

    [Fact]
    public void Validate_rechaza_un_sitio_web_que_supera_el_maximo_despues_del_recorte()
    {
        var result = _validator.TestValidate(Command(website: Padded(InsuranceProvider.MaxWebsiteLength + 1)));

        result.ShouldHaveValidationErrorFor(command => command.Website).WithErrorCode(InsuranceProviderErrors.WebsiteTooLong.Code);
    }

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public void Validate_con_correo_invalido_informa_el_campo_del_correo(string? contactEmail)
    {
        var result = _validator.TestValidate(Command(contactEmail: contactEmail));

        result.ShouldHaveValidationErrorFor(command => command.ContactEmail).WithErrorCode(CommonErrors.InvalidEmail.Code);
        result.Errors.Single().ErrorMessage.ShouldBe(CommonErrors.InvalidEmail.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_admite_el_sitio_web_ausente(string? website)
    {
        var result = _validator.TestValidate(Command(website: website));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_admite_los_limites_despues_del_recorte()
    {
        var result = _validator.TestValidate(new RegisterInsuranceProviderCommand(
            Padded(InsuranceProvider.MaxNameLength),
            Padded(InsuranceProvider.MaxContactPhoneLength),
            ContactEmail,
            Padded(InsuranceProvider.MaxAddressLength),
            PaddedHttpsUrl(InsuranceProvider.MaxWebsiteLength)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_rechaza_un_sitio_web_que_no_es_una_url_http()
    {
        var result = _validator.TestValidate(Command(website: "javascript:alert(1)"));

        result.ShouldHaveValidationErrorFor(command => command.Website).WithErrorCode(InsuranceProviderErrors.WebsiteInvalid.Code);
        result.Errors.Single().ErrorMessage.ShouldBe(InsuranceProviderErrors.WebsiteInvalid.Description);
    }

    [Fact]
    public void Validate_conserva_la_prioridad_del_primer_error_del_dominio()
    {
        var result = _validator.TestValidate(new RegisterInsuranceProviderCommand("", "", "sin-arroba", "", null));

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(RegisterInsuranceProviderCommand.Name));
        failure.ErrorCode.ShouldBe(InsuranceProviderErrors.NameRequired.Code);
    }

    [Fact]
    public void Validate_un_comando_completo_y_valido_no_tiene_errores()
    {
        var result = _validator.TestValidate(Command());

        result.ShouldNotHaveAnyValidationErrors();
    }

    // Texto de la longitud indicada rodeado de espacios, que el dominio recorta antes de medir.
    private static string Padded(int length) => "  " + new string('a', length) + "  ";

    // URL https de la longitud indicada rodeada de espacios, para probar el límite del sitio web.
    // El relleno va en la ruta para no depender de cómo Uri trata un nombre de host muy largo.
    private static string PaddedHttpsUrl(int length)
    {
        const string prefix = "https://seguros.com/";
        return "  " + prefix + new string('a', length - prefix.Length) + "  ";
    }

    private static RegisterInsuranceProviderCommand Command(
        string? name = Name,
        string? contactPhone = ContactPhone,
        string? contactEmail = ContactEmail,
        string? address = Address,
        string? website = Website) =>
        new(name!, contactPhone!, contactEmail!, address!, website);
}
