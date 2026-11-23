using FluentValidation.TestHelper;
using SGCM.Data.Application.People.Commands.UpdateInsuranceProvider;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class UpdateInsuranceProviderCommandValidatorTests
{
    private const string BasePath = "https://sitio.com/";

    private static readonly string?[] MissingValues = [null, "", "   ", " \t\r\n "];

    private readonly UpdateInsuranceProviderCommandValidator _validator = new();

    private static Change<string?> Set(string? value) => Change<string?>.Set(value);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_rechaza_el_id_invalido_antes_de_los_campos(int id)
    {
        // Los campos también son inválidos: solo debe reportarse el id.
        var command = new UpdateInsuranceProviderCommand(id, Only("name", ""));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(item => item.Id).WithErrorCode(InsuranceProviderErrors.InvalidId.Code);
        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(InsuranceProviderErrors.InvalidId.Description);
    }

    public static IEnumerable<object?[]> InvalidFields()
    {
        foreach (var value in MissingValues)
        {
            yield return ["name", value, "Name", "InsuranceProvider.NameRequired"];
            yield return ["phone", value, "ContactPhone", "InsuranceProvider.ContactPhoneRequired"];
            yield return ["email", value, "ContactEmail", "Common.InvalidEmail"];
            yield return ["address", value, "Address", "InsuranceProvider.AddressRequired"];
        }
        yield return ["name", new string('a', InsuranceProvider.MaxNameLength + 1), "Name", "InsuranceProvider.NameTooLong"];
        yield return ["phone", new string('1', InsuranceProvider.MaxContactPhoneLength + 1), "ContactPhone", "InsuranceProvider.ContactPhoneTooLong"];
        yield return ["email", "no-es-un-correo", "ContactEmail", "Common.InvalidEmail"];
        yield return ["address", new string('a', InsuranceProvider.MaxAddressLength + 1), "Address", "InsuranceProvider.AddressTooLong"];
        yield return ["website", BasePath + new string('a', InsuranceProvider.MaxWebsiteLength), "Website", "InsuranceProvider.WebsiteTooLong"];
        yield return ["website", "ftp://sitio.com", "Website", "InsuranceProvider.WebsiteInvalid"];
        yield return ["website", "www.sitio.com", "Website", "InsuranceProvider.WebsiteInvalid"];
        yield return ["none", null, "", "InsuranceProvider.NoFieldsProvided"];
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void Validate_reutiliza_el_error_el_codigo_el_mensaje_y_la_propiedad_del_dominio(
        string field, string? value, string property, string code)
    {
        var changes = Only(field, value);
        var expected = InsuranceProviderMother.Valid().Update(changes);

        var result = _validator.TestValidate(new UpdateInsuranceProviderCommand(42, changes));

        expected.IsFailure.ShouldBeTrue();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(property);
        failure.ErrorCode.ShouldBe(code);
        failure.ErrorMessage.ShouldBe(expected.Error.Description);
    }

    public static IEnumerable<object[]> PriorityCases()
    {
        // Banderas: nombre, teléfono, correo, dirección y sitio web inválidos, en ese orden.
        yield return [true, true, true, true, true, "Name", "InsuranceProvider.NameRequired"];
        yield return [false, true, true, true, true, "ContactPhone", "InsuranceProvider.ContactPhoneRequired"];
        yield return [false, false, true, true, true, "ContactEmail", "Common.InvalidEmail"];
        yield return [false, false, false, true, true, "Address", "InsuranceProvider.AddressRequired"];
        yield return [false, false, false, false, true, "Website", "InsuranceProvider.WebsiteInvalid"];
    }

    [Theory]
    [MemberData(nameof(PriorityCases))]
    public void Validate_con_varios_errores_reporta_solo_el_primero_en_el_orden_del_dominio(
        bool badName, bool badPhone, bool badEmail, bool badAddress, bool badWebsite, string property, string code)
    {
        var changes = new InsuranceProviderChanges(
            Set(badName ? "" : "Seguros del Caribe"), Set(badPhone ? "" : "809-555-0999"),
            Set(badEmail ? "no-es-un-correo" : "nuevo@caribe.com.do"), Set(badAddress ? "  " : "Calle Nueva 5"),
            Set(badWebsite ? "ftp://sitio.com" : "https://caribe.com.do"));

        var result = _validator.TestValidate(new UpdateInsuranceProviderCommand(42, changes));

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(property);
        failure.ErrorCode.ShouldBe(code);
    }

    [Fact]
    public void Validate_admite_los_limites_exactos_despues_de_recortar()
    {
        var changes = new InsuranceProviderChanges(
            Set(" \t" + new string('a', InsuranceProvider.MaxNameLength) + "  "),
            Set("  " + new string('1', InsuranceProvider.MaxContactPhoneLength) + " "),
            Set(" Nuevo@Caribe.COM.do "),
            Set(" \r\n" + new string('b', InsuranceProvider.MaxAddressLength) + " "),
            Set(" " + BasePath + new string('c', InsuranceProvider.MaxWebsiteLength - BasePath.Length) + "  "));

        _validator.TestValidate(new UpdateInsuranceProviderCommand(int.MaxValue, changes)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_admite_quitar_el_sitio_web(string? website)
    {
        _validator.TestValidate(new UpdateInsuranceProviderCommand(42, Only("website", website)))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("phone")]
    [InlineData("email")]
    [InlineData("address")]
    [InlineData("website")]
    public void Validate_ignora_los_campos_no_enviados_y_acepta_un_solo_campo_valido(string field)
    {
        var value = field switch
        {
            "name" => "Seguros del Caribe",
            "phone" => "809-555-0999",
            "email" => "nuevo@caribe.com.do",
            "address" => "Calle Nueva 5",
            _ => "https://caribe.com.do"
        };

        _validator.TestValidate(new UpdateInsuranceProviderCommand(42, Only(field, value))).ShouldNotHaveAnyValidationErrors();
    }

    private static InsuranceProviderChanges Only(string field, string? value) => field switch
    {
        "name" => default(InsuranceProviderChanges) with { Name = Set(value) },
        "phone" => default(InsuranceProviderChanges) with { ContactPhone = Set(value) },
        "email" => default(InsuranceProviderChanges) with { ContactEmail = Set(value) },
        "address" => default(InsuranceProviderChanges) with { Address = Set(value) },
        "website" => default(InsuranceProviderChanges) with { Website = Set(value) },
        "none" => default,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
