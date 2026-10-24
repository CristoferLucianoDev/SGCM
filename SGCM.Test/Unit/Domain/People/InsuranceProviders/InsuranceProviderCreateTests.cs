using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.InsuranceProviders;

public class InsuranceProviderCreateTests
{
    private const string ValidName = "Seguros Universal";
    private const string ValidPhone = "809-555-0101";
    private const string ValidEmail = "contacto@universal.com.do";
    private const string ValidAddress = "Av. Winston Churchill 1100, Santo Domingo";
    private const string ValidWebsite = "https://www.universal.com.do";

    [Fact]
    public void Create_con_datos_validos_devuelve_un_proveedor_activo()
    {
        var result = InsuranceProvider.Create(ValidName, ValidPhone, ValidEmail, ValidAddress, ValidWebsite);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        result.Value.Name.ShouldBe(ValidName);
        result.Value.ContactPhone.ShouldBe(ValidPhone);
        result.Value.ContactEmail.Value.ShouldBe(ValidEmail);
        result.Value.Address.ShouldBe(ValidAddress);
        result.Value.Website.ShouldBe(ValidWebsite);
        result.Value.IsActive.ShouldBeTrue();
        result.Value.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_recorta_los_extremos_y_normaliza_el_correo()
    {
        var result = InsuranceProvider.Create(
            "  Seguros Universal\t", " 809-555-0101 ", " Contacto@Universal.com.do ", " Av. Principal \r\n", " https://universal.do ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Seguros Universal");
        result.Value.ContactPhone.ShouldBe("809-555-0101");
        result.Value.ContactEmail.Value.ShouldBe("contacto@universal.com.do");
        result.Value.Address.ShouldBe("Av. Principal");
        result.Value.Website.ShouldBe("https://universal.do");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \r\n\t ")]
    public void Create_sin_sitio_web_lo_deja_vacio(string? website)
    {
        var result = InsuranceProvider.Create(ValidName, ValidPhone, ValidEmail, ValidAddress, website);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Website.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \r\n\t ")]
    public void Create_sin_nombre_devuelve_error(string? name)
    {
        var result = InsuranceProvider.Create(name, ValidPhone, ValidEmail, ValidAddress, null);

        result.Error.ShouldBe(InsuranceProviderErrors.NameRequired);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_sin_telefono_devuelve_error(string? phone)
    {
        var result = InsuranceProvider.Create(ValidName, phone, ValidEmail, ValidAddress, null);

        result.Error.ShouldBe(InsuranceProviderErrors.ContactPhoneRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-correo")]
    [InlineData("Nombre <contacto@universal.com.do>")]
    public void Create_con_correo_invalido_devuelve_error(string? email)
    {
        var result = InsuranceProvider.Create(ValidName, ValidPhone, email, ValidAddress, null);

        result.Error.ShouldBe(CommonErrors.InvalidEmail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_sin_direccion_devuelve_error(string? address)
    {
        var result = InsuranceProvider.Create(ValidName, ValidPhone, ValidEmail, address, null);

        result.Error.ShouldBe(InsuranceProviderErrors.AddressRequired);
    }

    [Fact]
    public void Create_con_nombre_demasiado_largo_devuelve_error()
    {
        var name = new string('a', InsuranceProvider.MaxNameLength + 1);

        var result = InsuranceProvider.Create(name, ValidPhone, ValidEmail, ValidAddress, null);

        result.Error.ShouldBe(InsuranceProviderErrors.NameTooLong);
    }

    [Fact]
    public void Create_con_telefono_demasiado_largo_devuelve_error()
    {
        var phone = new string('1', InsuranceProvider.MaxContactPhoneLength + 1);

        var result = InsuranceProvider.Create(ValidName, phone, ValidEmail, ValidAddress, null);

        result.Error.ShouldBe(InsuranceProviderErrors.ContactPhoneTooLong);
    }

    [Fact]
    public void Create_con_direccion_demasiado_larga_devuelve_error()
    {
        var address = new string('a', InsuranceProvider.MaxAddressLength + 1);

        var result = InsuranceProvider.Create(ValidName, ValidPhone, ValidEmail, address, null);

        result.Error.ShouldBe(InsuranceProviderErrors.AddressTooLong);
    }

    [Fact]
    public void Create_con_sitio_web_demasiado_largo_devuelve_error()
    {
        var website = new string('a', InsuranceProvider.MaxWebsiteLength + 1);

        var result = InsuranceProvider.Create(ValidName, ValidPhone, ValidEmail, ValidAddress, website);

        result.Error.ShouldBe(InsuranceProviderErrors.WebsiteTooLong);
    }

    [Fact]
    public void Create_acepta_textos_con_la_longitud_maxima_aunque_tengan_espacios_en_los_extremos()
    {
        var name = $"  {new string('a', InsuranceProvider.MaxNameLength)}  ";
        var phone = new string('1', InsuranceProvider.MaxContactPhoneLength);
        var address = new string('b', InsuranceProvider.MaxAddressLength);
        var website = new string('c', InsuranceProvider.MaxWebsiteLength);

        var result = InsuranceProvider.Create(name, phone, ValidEmail, address, website);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.Length.ShouldBe(InsuranceProvider.MaxNameLength);
    }

    [Fact]
    public void Create_devuelve_el_primer_error_en_el_orden_de_los_campos()
    {
        var result = InsuranceProvider.Create(null, null, null, null, null);

        result.Error.ShouldBe(InsuranceProviderErrors.NameRequired);
    }
}
