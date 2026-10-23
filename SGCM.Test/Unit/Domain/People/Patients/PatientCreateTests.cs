
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Patients;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Patients;

/// <summary>
/// Pruebas de las reglas de creación del agregado Patient.
/// </summary>
public class PatientCreateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static Result<Patient> CreatePatient(
        int userId = 1,
        string? firstName = "Ana",
        string? lastName = "Pérez",
        DateOnly? birthDate = null,
        string? phoneNumber = "8095551234",
        string? address = null,
        string? emergencyContactName = null,
        string? emergencyContactPhone = null,
        int? insuranceProviderId = null,
        string? policyNumber = null)
    {
        return Patient.Create(
            userId,
            firstName,
            lastName,
            birthDate ?? new DateOnly(2000, 5, 10),
            phoneNumber,
            address,
            emergencyContactName,
            emergencyContactPhone,
            insuranceProviderId,
            policyNumber,
            Now);
    }

    [Fact]
    public void Create_con_datos_validos_crea_el_paciente()
    {
        var result = CreatePatient();

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(1);
        result.Value.FirstName.ShouldBe("Ana");
        result.Value.LastName.ShouldBe("Pérez");
        result.Value.BirthDate.ShouldBe(new DateOnly(2000, 5, 10));
        result.Value.PhoneNumber.ShouldBe("8095551234");
        result.Value.IsActive.ShouldBeTrue();
        result.Value.CreatedAt.ShouldBe(Now);
        result.Value.CreatedBy.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_con_usuario_invalido_falla(int userId)
    {
        var result = CreatePatient(userId: userId);

        result.Error.ShouldBe(PatientErrors.InvalidUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_sin_nombre_falla(string? firstName)
    {
        var result = CreatePatient(firstName: firstName);

        result.Error.ShouldBe(PatientErrors.FirstNameRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_sin_apellido_falla(string? lastName)
    {
        var result = CreatePatient(lastName: lastName);

        result.Error.ShouldBe(PatientErrors.LastNameRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_sin_telefono_falla(string? phoneNumber)
    {
        var result = CreatePatient(phoneNumber: phoneNumber);

        result.Error.ShouldBe(PatientErrors.PhoneRequired);
    }

    [Fact]
    public void Create_con_fecha_futura_falla()
    {
        var result = CreatePatient(
            birthDate: new DateOnly(2026, 10, 20));

        result.Error.ShouldBe(PatientErrors.InvalidBirthDate);
    }

    [Fact]
    public void Create_con_fecha_predeterminada_falla()
    {
        var result = CreatePatient(birthDate: DateOnly.MinValue);

        result.Error.ShouldBe(PatientErrors.InvalidBirthDate);
    }

    [Fact]
    public void Create_con_nombre_demasiado_largo_falla()
    {
        var result = CreatePatient(firstName: new string('A', 101));

        result.Error.ShouldBe(PatientErrors.FirstNameTooLong);
    }

    [Fact]
    public void Create_con_apellido_demasiado_largo_falla()
    {
        var result = CreatePatient(lastName: new string('A', 101));

        result.Error.ShouldBe(PatientErrors.LastNameTooLong);
    }

    [Fact]
    public void Create_con_telefono_demasiado_largo_falla()
    {
        var result = CreatePatient(phoneNumber: new string('1', 21));

        result.Error.ShouldBe(PatientErrors.PhoneTooLong);
    }

    [Fact]
    public void Create_con_direccion_demasiado_larga_falla()
    {
        var result = CreatePatient(address: new string('A', 251));

        result.Error.ShouldBe(PatientErrors.AddressTooLong);
    }

    [Fact]
    public void Create_con_contacto_de_emergencia_demasiado_largo_falla()
    {
        var result = CreatePatient(
            emergencyContactName: new string('A', 151));

        result.Error.ShouldBe(PatientErrors.EmergencyContactNameTooLong);
    }

    [Fact]
    public void Create_con_telefono_de_emergencia_demasiado_largo_falla()
    {
        var result = CreatePatient(
            emergencyContactPhone: new string('1', 21));

        result.Error.ShouldBe(PatientErrors.EmergencyContactPhoneTooLong);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_con_proveedor_de_seguro_invalido_falla(int providerId)
    {
        var result = CreatePatient(insuranceProviderId: providerId);

        result.Error.ShouldBe(PatientErrors.InvalidInsuranceProvider);
    }

    [Fact]
    public void Create_con_seguro_sin_poliza_falla()
    {
        var result = CreatePatient(insuranceProviderId: 2);

        result.Error.ShouldBe(PatientErrors.PolicyNumberRequired);
    }

    [Fact]
    public void Create_con_poliza_demasiado_larga_falla()
    {
        var result = CreatePatient(
            insuranceProviderId: 2,
            policyNumber: new string('P', 51));

        result.Error.ShouldBe(PatientErrors.PolicyNumberTooLong);
    }

    [Fact]
    public void Create_con_seguro_y_poliza_validos_crea_el_paciente()
    {
        var result = CreatePatient(
            insuranceProviderId: 2,
            policyNumber: "POL-12345");

        result.IsSuccess.ShouldBeTrue();
        result.Value.InsuranceProviderId.ShouldBe(2);
        result.Value.PolicyNumber.ShouldBe("POL-12345");
    }

    [Fact]
    public void Create_normaliza_los_espacios_en_los_campos()
    {
        var result = CreatePatient(
            firstName: "  Ana  ",
            lastName: "  Pérez  ",
            phoneNumber: "  8095551234  ",
            address: "  Santo Domingo  ",
            emergencyContactName: "  Luis  ",
            emergencyContactPhone: "  8095555678  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.FirstName.ShouldBe("Ana");
        result.Value.LastName.ShouldBe("Pérez");
        result.Value.PhoneNumber.ShouldBe("8095551234");
        result.Value.Address.ShouldBe("Santo Domingo");
        result.Value.EmergencyContactName.ShouldBe("Luis");
        result.Value.EmergencyContactPhone.ShouldBe("8095555678");
    }

    [Fact]
    public void Create_convierte_campos_opcionales_vacios_en_null()
    {
        var result = CreatePatient(
            address: " ",
            emergencyContactName: "",
            emergencyContactPhone: "  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Address.ShouldBeNull();
        result.Value.EmergencyContactName.ShouldBeNull();
        result.Value.EmergencyContactPhone.ShouldBeNull();
    }

    [Fact]
    public void Create_no_emite_eventos_de_dominio()
    {
        var result = CreatePatient();

        result.IsSuccess.ShouldBeTrue();
        result.Value.DomainEvents.ShouldBeEmpty();
    }
}
