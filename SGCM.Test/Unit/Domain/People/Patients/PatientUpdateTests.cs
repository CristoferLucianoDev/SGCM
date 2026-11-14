using SGCM.Data.Domain.People.Patients;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Patients;

/// <summary>
/// Pruebas de la actualización de contacto y seguro del agregado Patient.
/// </summary>
public class PatientUpdateTests
{
    private const int UserId = 1;
    private const int InsuranceProviderId = 3;
    private const int OtherInsuranceProviderId = 5;

    private const string NewPhone = "8295550111";
    private const string NewAddress = "Av. Independencia 25, Santo Domingo";
    private const string NewContactName = "María Gómez";
    private const string NewContactPhone = "8495550122";
    private const string NewPolicy = "POL-9999";

    [Fact]
    public void UpdateContact_actualiza_telefono_direccion_y_contacto_de_emergencia()
    {
        var patient = PatientMother.WithRequiredData(UserId);

        var result = patient.UpdateContact($"  {NewPhone}  ", $" {NewAddress} ", $" {NewContactName} ", $" {NewContactPhone} ");

        result.IsSuccess.ShouldBeTrue();
        patient.PhoneNumber.ShouldBe(NewPhone);
        patient.Address.ShouldBe(NewAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(NewContactName);
        patient.EmergencyContact.PhoneNumber.ShouldBe(NewContactPhone);
    }

    [Fact]
    public void UpdateContact_conserva_la_identidad_y_el_seguro()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        patient.UpdateContact(NewPhone, NewAddress, NewContactName, NewContactPhone).IsSuccess.ShouldBeTrue();

        patient.UserId.ShouldBe(UserId);
        patient.FirstName.ShouldBe(PatientMother.DefaultFirstName);
        patient.LastName.ShouldBe(PatientMother.DefaultLastName);
        patient.BirthDate.ShouldBe(PatientMother.DefaultBirthDate);
        patient.InsuranceProviderId.ShouldBe(InsuranceProviderId);
        patient.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
    }

    [Fact]
    public void UpdateContact_sin_direccion_ni_contacto_los_deja_vacios()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        patient.UpdateContact(NewPhone, address: null, emergencyContactName: "  ", emergencyContactPhone: null)
            .IsSuccess.ShouldBeTrue();

        patient.Address.ShouldBeNull();
        patient.EmergencyContact.ShouldBeNull();
    }

    [Fact]
    public void UpdateContact_admite_las_longitudes_maximas()
    {
        var patient = PatientMother.WithRequiredData(UserId);

        var result = patient.UpdateContact(
            new string('1', 20), new string('A', 250), new string('A', 150), new string('1', 20));

        result.IsSuccess.ShouldBeTrue();
        patient.PhoneNumber.ShouldBe(new string('1', 20));
        patient.Address.ShouldBe(new string('A', 250));
        patient.EmergencyContact!.Name.ShouldBe(new string('A', 150));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateContact_sin_telefono_se_rechaza_sin_cambiar_nada(string? phoneNumber)
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.UpdateContact(phoneNumber, NewAddress, NewContactName, NewContactPhone);

        result.Error.ShouldBe(PatientErrors.PhoneRequired);
        AssertContactUnchanged(patient);
    }

    [Fact]
    public void UpdateContact_rechaza_telefono_de_21_caracteres()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.UpdateContact(new string('1', 21), NewAddress, NewContactName, NewContactPhone);

        result.Error.ShouldBe(PatientErrors.PhoneTooLong);
        AssertContactUnchanged(patient);
    }

    [Fact]
    public void UpdateContact_rechaza_direccion_de_251_caracteres()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.UpdateContact(NewPhone, new string('A', 251), NewContactName, NewContactPhone);

        result.Error.ShouldBe(PatientErrors.AddressTooLong);
        AssertContactUnchanged(patient);
    }

    [Theory]
    [InlineData(NewContactName, null)]
    [InlineData(null, NewContactPhone)]
    public void UpdateContact_rechaza_contacto_de_emergencia_incompleto(string? name, string? phone)
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.UpdateContact(NewPhone, NewAddress, name, phone);

        result.Error.ShouldBe(PatientErrors.IncompleteEmergencyContact);
        AssertContactUnchanged(patient);
    }

    [Fact]
    public void UpdateContact_rechaza_nombre_de_contacto_de_151_caracteres()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.UpdateContact(NewPhone, NewAddress, new string('A', 151), NewContactPhone);

        result.Error.ShouldBe(PatientErrors.EmergencyContactNameTooLong);
        AssertContactUnchanged(patient);
    }

    [Fact]
    public void UpdateContact_rechaza_telefono_de_contacto_de_21_caracteres()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.UpdateContact(NewPhone, NewAddress, NewContactName, new string('1', 21));

        result.Error.ShouldBe(PatientErrors.EmergencyContactPhoneTooLong);
        AssertContactUnchanged(patient);
    }

    [Fact]
    public void ChangeInsurance_asigna_un_seguro_a_un_paciente_sin_seguro()
    {
        var patient = PatientMother.WithRequiredData(UserId);

        var result = patient.ChangeInsurance(InsuranceProviderId, $"  {NewPolicy}  ");

        result.IsSuccess.ShouldBeTrue();
        patient.InsuranceProviderId.ShouldBe(InsuranceProviderId);
        patient.PolicyNumber.ShouldBe(NewPolicy);
    }

    [Fact]
    public void ChangeInsurance_cambia_de_proveedor_y_conserva_el_contacto()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        patient.ChangeInsurance(OtherInsuranceProviderId, NewPolicy).IsSuccess.ShouldBeTrue();

        patient.InsuranceProviderId.ShouldBe(OtherInsuranceProviderId);
        patient.PolicyNumber.ShouldBe(NewPolicy);
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.EmergencyContact!.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
    }

    [Fact]
    public void ChangeInsurance_admite_poliza_de_50_caracteres()
    {
        var patient = PatientMother.WithRequiredData(UserId);

        patient.ChangeInsurance(InsuranceProviderId, new string('A', 50)).IsSuccess.ShouldBeTrue();

        patient.PolicyNumber.ShouldBe(new string('A', 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ChangeInsurance_rechaza_un_proveedor_no_valido_sin_cambiar_nada(int insuranceProviderId)
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.ChangeInsurance(insuranceProviderId, NewPolicy);

        result.Error.ShouldBe(PatientErrors.InvalidInsuranceProvider);
        AssertInsuranceUnchanged(patient);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ChangeInsurance_sin_poliza_se_rechaza_sin_cambiar_nada(string? policyNumber)
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.ChangeInsurance(OtherInsuranceProviderId, policyNumber);

        result.Error.ShouldBe(PatientErrors.PolicyNumberRequired);
        AssertInsuranceUnchanged(patient);
    }

    [Fact]
    public void ChangeInsurance_rechaza_poliza_de_51_caracteres()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        var result = patient.ChangeInsurance(OtherInsuranceProviderId, new string('A', 51));

        result.Error.ShouldBe(PatientErrors.PolicyNumberTooLong);
        AssertInsuranceUnchanged(patient);
    }

    [Fact]
    public void RemoveInsurance_deja_el_perfil_sin_proveedor_ni_poliza()
    {
        var patient = PatientMother.WithAllData(UserId, InsuranceProviderId);

        patient.RemoveInsurance();

        patient.InsuranceProviderId.ShouldBeNull();
        patient.PolicyNumber.ShouldBeNull();
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.EmergencyContact.ShouldNotBeNull();
    }

    [Fact]
    public void RemoveInsurance_en_un_paciente_sin_seguro_no_cambia_nada()
    {
        var patient = PatientMother.WithRequiredData(UserId);

        patient.RemoveInsurance();

        patient.InsuranceProviderId.ShouldBeNull();
        patient.PolicyNumber.ShouldBeNull();
    }

    private static void AssertContactUnchanged(Patient patient)
    {
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        patient.Address.ShouldBe(PatientMother.DefaultAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(PatientMother.DefaultEmergencyContactName);
        patient.EmergencyContact.PhoneNumber.ShouldBe(PatientMother.DefaultEmergencyContactPhone);
    }

    private static void AssertInsuranceUnchanged(Patient patient)
    {
        patient.InsuranceProviderId.ShouldBe(InsuranceProviderId);
        patient.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
    }
}
