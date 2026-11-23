using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.InsuranceProviders;

public sealed class InsuranceProviderUpdateTests
{
    private const string NewName = "Seguros del Caribe";
    private const string NewPhone = "809-555-0999";
    private const string NewEmail = "nuevo@caribe.com.do";
    private const string NewAddress = "Calle Nueva 5, Santiago";
    private const string NewWebsite = "https://www.caribe.com.do";
    private const string BasePath = "https://sitio.com/";
    private const string BadEmail = "no-es-un-correo";
    private const string BadWebsite = "ftp://sitio.com";

    private static readonly string?[] MissingValues = [null, "", "   ", "\t", " \r\n\t "];

    private static readonly InsuranceProviderChanges None = default;

    private static Change<string?> Set(string? value) => Change<string?>.Set(value);

    // Cambia un campo por su nombre para poder parametrizar las pruebas con datos primitivos.
    private static InsuranceProviderChanges Only(string field, string? value) => field switch
    {
        "name" => None with { Name = Set(value) },
        "phone" => None with { ContactPhone = Set(value) },
        "email" => None with { ContactEmail = Set(value) },
        "address" => None with { Address = Set(value) },
        "website" => None with { Website = Set(value) },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    [Fact]
    public void Update_solo_el_nombre_conserva_los_otros_cuatro()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(None with { Name = Set(NewName) });

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        provider.Name.ShouldBe(NewName);
        provider.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        provider.ContactEmail.Value.ShouldBe(InsuranceProviderMother.DefaultContactEmail);
        provider.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        provider.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
    }

    [Fact]
    public void Update_solo_el_telefono_conserva_los_otros_cuatro()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(None with { ContactPhone = Set(NewPhone) });

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(InsuranceProviderMother.DefaultName);
        provider.ContactPhone.ShouldBe(NewPhone);
        provider.ContactEmail.Value.ShouldBe(InsuranceProviderMother.DefaultContactEmail);
        provider.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        provider.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
    }

    [Fact]
    public void Update_solo_el_correo_conserva_los_otros_cuatro()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(None with { ContactEmail = Set(NewEmail) });

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(InsuranceProviderMother.DefaultName);
        provider.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        provider.ContactEmail.Value.ShouldBe(NewEmail);
        provider.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        provider.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
    }

    [Fact]
    public void Update_solo_la_direccion_conserva_los_otros_cuatro()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(None with { Address = Set(NewAddress) });

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(InsuranceProviderMother.DefaultName);
        provider.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        provider.ContactEmail.Value.ShouldBe(InsuranceProviderMother.DefaultContactEmail);
        provider.Address.ShouldBe(NewAddress);
        provider.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
    }

    [Fact]
    public void Update_solo_el_sitio_web_conserva_los_otros_cuatro()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(None with { Website = Set(NewWebsite) });

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(InsuranceProviderMother.DefaultName);
        provider.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        provider.ContactEmail.Value.ShouldBe(InsuranceProviderMother.DefaultContactEmail);
        provider.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        provider.Website.ShouldBe(NewWebsite);
    }

    [Fact]
    public void Update_cambia_todos_los_campos_a_la_vez()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(new InsuranceProviderChanges(
            Set(NewName), Set(NewPhone), Set(NewEmail), Set(NewAddress), Set(NewWebsite)));

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(NewName);
        provider.ContactPhone.ShouldBe(NewPhone);
        provider.ContactEmail.Value.ShouldBe(NewEmail);
        provider.Address.ShouldBe(NewAddress);
        provider.Website.ShouldBe(NewWebsite);
    }

    [Fact]
    public void Update_recorta_los_extremos_y_normaliza_el_correo_de_los_campos_enviados()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(new InsuranceProviderChanges(
            Set("  Seguros del Caribe\t"), Set(" 809-555-0999 "), Set(" Nuevo@Caribe.COM.do "),
            Set(" Calle Nueva 5 \r\n"), Set(" https://www.caribe.com.do \t")));

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe("Seguros del Caribe");
        provider.ContactPhone.ShouldBe("809-555-0999");
        provider.ContactEmail.Value.ShouldBe("nuevo@caribe.com.do");
        provider.Address.ShouldBe("Calle Nueva 5");
        provider.Website.ShouldBe("https://www.caribe.com.do");
    }

    [Fact]
    public void Update_sin_ningun_campo_enviado_devuelve_NoFieldsProvided_y_no_cambia_nada()
    {
        var provider = InsuranceProviderMother.Valid();
        var before = Snapshot(provider);
        var explicitlyUnchanged = new InsuranceProviderChanges(
            Change<string?>.Unchanged, Change<string?>.Unchanged, Change<string?>.Unchanged,
            Change<string?>.Unchanged, Change<string?>.Unchanged);

        foreach (var changes in new[] { default, explicitlyUnchanged })
        {
            changes.HasChanges.ShouldBeFalse();

            var result = provider.Update(changes);

            result.IsFailure.ShouldBeTrue();
            result.Error.ShouldBe(InsuranceProviderErrors.NoFieldsProvided);
            result.Error.Type.ShouldBe(ErrorType.Validation);
            result.Error.Code.ShouldBe("InsuranceProvider.NoFieldsProvided");
            Snapshot(provider).ShouldBe(before);
        }
    }

    public static IEnumerable<object?[]> MissingRequiredFields()
    {
        foreach (var value in MissingValues)
        {
            yield return ["name", value, "InsuranceProvider.NameRequired"];
            yield return ["phone", value, "InsuranceProvider.ContactPhoneRequired"];
            yield return ["email", value, "Common.InvalidEmail"];
            yield return ["address", value, "InsuranceProvider.AddressRequired"];
        }
    }

    [Theory]
    [MemberData(nameof(MissingRequiredFields))]
    public void Update_con_un_obligatorio_nulo_vacio_o_en_blanco_devuelve_su_error_sin_cambiar_nada(
        string field, string? value, string expectedCode)
    {
        var provider = InsuranceProviderMother.Valid();
        var before = Snapshot(provider);

        var result = provider.Update(Only(field, value));

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
        Snapshot(provider).ShouldBe(before);
    }

    public static IEnumerable<object[]> TooLongFields()
    {
        yield return ["name", InsuranceProvider.MaxNameLength, "InsuranceProvider.NameTooLong"];
        yield return ["phone", InsuranceProvider.MaxContactPhoneLength, "InsuranceProvider.ContactPhoneTooLong"];
        yield return ["address", InsuranceProvider.MaxAddressLength, "InsuranceProvider.AddressTooLong"];
        yield return ["website", InsuranceProvider.MaxWebsiteLength, "InsuranceProvider.WebsiteTooLong"];
    }

    [Theory]
    [MemberData(nameof(TooLongFields))]
    public void Update_con_un_texto_un_caracter_sobre_el_maximo_devuelve_su_TooLong_sin_cambiar_nada(
        string field, int maxLength, string expectedCode)
    {
        var provider = InsuranceProviderMother.Valid();
        var before = Snapshot(provider);

        var result = provider.Update(Only(field, ValueOfLength(field, maxLength + 1)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
        Snapshot(provider).ShouldBe(before);
    }

    [Theory]
    [MemberData(nameof(TooLongFields))]
    public void Update_acepta_un_texto_exactamente_en_el_maximo_incluso_con_espacios_en_los_extremos(
        string field, int maxLength, string _)
    {
        var provider = InsuranceProviderMother.Valid();
        var value = ValueOfLength(field, maxLength);

        var result = provider.Update(Only(field, $" \t{value}\r\n "));

        result.IsSuccess.ShouldBeTrue();
        ValueOf(provider, field).ShouldBe(value);
        ValueOf(provider, field)!.Length.ShouldBe(maxLength);
    }

    [Theory]
    [InlineData("ftp://sitio.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hola")]
    [InlineData("/ruta/relativa")]
    [InlineData("www.sitio.com")]
    [InlineData("sitio.com")]
    public void Update_con_un_sitio_web_que_no_es_http_ni_https_devuelve_WebsiteInvalid_sin_cambiar_nada(string website)
    {
        var provider = InsuranceProviderMother.Valid();
        var before = Snapshot(provider);

        var result = provider.Update(None with { Website = Set(website) });

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(InsuranceProviderErrors.WebsiteInvalid);
        Snapshot(provider).ShouldBe(before);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \r\n\t ")]
    public void Update_con_el_sitio_web_nulo_vacio_o_en_blanco_quita_el_sitio_existente(string? website)
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(None with { Website = Set(website) });

        result.IsSuccess.ShouldBeTrue();
        provider.Website.ShouldBeNull();
        provider.Name.ShouldBe(InsuranceProviderMother.DefaultName);
    }

    [Fact]
    public void Update_sin_enviar_el_sitio_web_lo_conserva()
    {
        var provider = InsuranceProviderMother.Valid();

        provider.Update(None with { Name = Set(NewName) }).IsSuccess.ShouldBeTrue();

        provider.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_con_el_sitio_web_vacio_en_un_proveedor_sin_sitio_lo_deja_nulo(string? website)
    {
        var provider = InsuranceProviderMother.Valid(website: null);

        var result = provider.Update(None with { Website = Set(website) });

        result.IsSuccess.ShouldBeTrue();
        provider.Website.ShouldBeNull();
    }

    [Fact]
    public void Update_con_un_sitio_web_en_un_proveedor_sin_sitio_lo_agrega()
    {
        var provider = InsuranceProviderMother.Valid(website: null);

        var result = provider.Update(None with { Website = Set(NewWebsite) });

        result.IsSuccess.ShouldBeTrue();
        provider.Website.ShouldBe(NewWebsite);
    }

    [Fact]
    public void Update_es_atomico_un_nombre_valido_junto_a_un_sitio_web_invalido_no_cambia_el_nombre()
    {
        var provider = InsuranceProviderMother.Valid();
        var before = Snapshot(provider);

        var result = provider.Update(None with { Name = Set(NewName), Website = Set(BadWebsite) });

        result.Error.ShouldBe(InsuranceProviderErrors.WebsiteInvalid);
        provider.Name.ShouldBe(InsuranceProviderMother.DefaultName);
        Snapshot(provider).ShouldBe(before);
    }

    public static IEnumerable<object[]> ErrorPriorityCases()
    {
        // Banderas: nombre, teléfono, correo, dirección y sitio web inválidos, en ese orden.
        yield return [true, true, true, true, true, "InsuranceProvider.NameRequired"];
        yield return [false, true, true, true, true, "InsuranceProvider.ContactPhoneRequired"];
        yield return [false, false, true, true, true, "Common.InvalidEmail"];
        yield return [false, false, false, true, true, "InsuranceProvider.AddressRequired"];
        yield return [false, false, false, false, true, "InsuranceProvider.WebsiteInvalid"];
        yield return [true, false, false, false, true, "InsuranceProvider.NameRequired"];
        yield return [false, false, true, false, true, "Common.InvalidEmail"];
    }

    [Theory]
    [MemberData(nameof(ErrorPriorityCases))]
    public void Update_con_varios_errores_devuelve_el_primero_en_el_orden_nombre_telefono_correo_direccion_sitio(
        bool badName, bool badPhone, bool badEmail, bool badAddress, bool badWebsite, string expectedCode)
    {
        var provider = InsuranceProviderMother.Valid();
        var before = Snapshot(provider);
        var changes = new InsuranceProviderChanges(
            Set(badName ? "" : NewName), Set(badPhone ? "" : NewPhone), Set(badEmail ? BadEmail : NewEmail),
            Set(badAddress ? "   " : NewAddress), Set(badWebsite ? BadWebsite : NewWebsite));

        var result = provider.Update(changes);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(expectedCode);
        Snapshot(provider).ShouldBe(before);
    }

    [Fact]
    public void Update_con_los_mismos_valores_actuales_es_exito_y_no_cambia_nada()
    {
        var provider = InsuranceProviderMother.Valid();
        var before = Snapshot(provider);

        var result = provider.Update(new InsuranceProviderChanges(
            Set(provider.Name), Set(provider.ContactPhone), Set(provider.ContactEmail.Value),
            Set(provider.Address), Set(provider.Website)));

        result.IsSuccess.ShouldBeTrue();
        Snapshot(provider).ShouldBe(before);
        provider.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_de_un_proveedor_inactivo_funciona_y_lo_deja_inactivo()
    {
        var provider = InsuranceProviderMother.Valid();
        provider.Deactivate().IsSuccess.ShouldBeTrue();

        var result = provider.Update(None with { Name = Set(NewName), Website = Set(null) });

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(NewName);
        provider.Website.ShouldBeNull();
        provider.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Update_no_cambia_el_estado_de_un_proveedor_activo_ni_emite_eventos()
    {
        var provider = InsuranceProviderMother.Valid();

        var result = provider.Update(new InsuranceProviderChanges(
            Set(NewName), Set(NewPhone), Set(NewEmail), Set(NewAddress), Set(NewWebsite)));

        result.IsSuccess.ShouldBeTrue();
        provider.IsActive.ShouldBeTrue();
        provider.DomainEvents.ShouldBeEmpty();
    }

    // Texto válido de la longitud pedida para el campo: el sitio web necesita una URL, así que se rellena la ruta.
    private static string ValueOfLength(string field, int length) => field == "website"
        ? BasePath + new string('a', length - BasePath.Length)
        : new string('a', length);

    private static string? ValueOf(InsuranceProvider provider, string field) => field switch
    {
        "name" => provider.Name,
        "phone" => provider.ContactPhone,
        "address" => provider.Address,
        "website" => provider.Website,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static (string Name, string Phone, string Email, string Address, string? Website, bool IsActive) Snapshot(
        InsuranceProvider provider) =>
        (provider.Name, provider.ContactPhone, provider.ContactEmail.Value, provider.Address, provider.Website,
            provider.IsActive);
}
