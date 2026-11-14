using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.UpdatePatientProfile;
using SGCM.Data.Domain.People.Patients;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public class UpdatePatientProfileCommandHandlerTests
{
    private const int UserId = 7;
    private const int CurrentProviderId = 3;
    private const int NewProviderId = 5;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patientRepository = Substitute.For<IPatientRepository>();
    private readonly IInsuranceProviderReader _insuranceProviderReader = Substitute.For<IInsuranceProviderReader>();

    public UpdatePatientProfileCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _insuranceProviderReader.IsActiveAsync(NewProviderId, Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task Handle_actualiza_el_contacto_del_perfil_del_usuario_autenticado()
    {
        var patient = GivenPatient(PatientMother.WithRequiredData(UserId));
        using var cancellation = new CancellationTokenSource();
        var command = UpdatePatientProfileCommandMother.WithAllData(NewProviderId);

        var result = await CreateHandler().HandleAsync(command, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        await _patientRepository.Received(1).GetByUserIdAsync(UserId, cancellation.Token);
        patient.PhoneNumber.ShouldBe(UpdatePatientProfileCommandMother.NewPhoneNumber);
        patient.Address.ShouldBe(UpdatePatientProfileCommandMother.NewAddress);
        patient.EmergencyContact.ShouldNotBeNull();
        patient.EmergencyContact.Name.ShouldBe(UpdatePatientProfileCommandMother.NewEmergencyContactName);
        patient.InsuranceProviderId.ShouldBe(NewProviderId);
        patient.PolicyNumber.ShouldBe(UpdatePatientProfileCommandMother.NewPolicyNumber);
    }

    [Fact]
    public async Task Handle_conserva_nombre_apellido_y_fecha_de_nacimiento()
    {
        var patient = GivenPatient(PatientMother.WithAllData(UserId, CurrentProviderId));

        await CreateHandler().HandleAsync(UpdatePatientProfileCommandMother.WithAllData(NewProviderId), CancellationToken.None);

        patient.FirstName.ShouldBe(PatientMother.DefaultFirstName);
        patient.LastName.ShouldBe(PatientMother.DefaultLastName);
        patient.BirthDate.ShouldBe(PatientMother.DefaultBirthDate);
    }

    [Fact]
    public async Task Handle_sin_proveedor_quita_el_seguro()
    {
        var patient = GivenPatient(PatientMother.WithAllData(UserId, CurrentProviderId));

        var result = await CreateHandler().HandleAsync(
            UpdatePatientProfileCommandMother.WithRequiredData(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        patient.InsuranceProviderId.ShouldBeNull();
        patient.PolicyNumber.ShouldBeNull();
        patient.Address.ShouldBeNull();
        patient.EmergencyContact.ShouldBeNull();
        await _insuranceProviderReader.DidNotReceive().IsActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_con_el_mismo_proveedor_no_lo_valida_como_activo()
    {
        // Un proveedor desactivado después de elegirlo no impide actualizar los demás datos.
        _insuranceProviderReader.IsActiveAsync(CurrentProviderId, Arg.Any<CancellationToken>()).Returns(false);
        var patient = GivenPatient(PatientMother.WithAllData(UserId, CurrentProviderId));

        var result = await CreateHandler().HandleAsync(
            UpdatePatientProfileCommandMother.WithAllData(CurrentProviderId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        patient.InsuranceProviderId.ShouldBe(CurrentProviderId);
        patient.PolicyNumber.ShouldBe(UpdatePatientProfileCommandMother.NewPolicyNumber);
        await _insuranceProviderReader.DidNotReceive().IsActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_con_un_proveedor_nuevo_inactivo_o_inexistente_responde_proveedor_invalido()
    {
        _insuranceProviderReader.IsActiveAsync(NewProviderId, Arg.Any<CancellationToken>()).Returns(false);
        var patient = GivenPatient(PatientMother.WithAllData(UserId, CurrentProviderId));

        var result = await CreateHandler().HandleAsync(
            UpdatePatientProfileCommandMother.WithAllData(NewProviderId), CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.InvalidInsuranceProvider);
        patient.InsuranceProviderId.ShouldBe(CurrentProviderId);
        patient.PolicyNumber.ShouldBe(PatientMother.DefaultPolicyNumber);
    }

    [Fact]
    public async Task Handle_sin_perfil_responde_perfil_no_encontrado()
    {
        _patientRepository.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Patient?)null);

        var result = await CreateHandler().HandleAsync(
            UpdatePatientProfileCommandMother.WithRequiredData(), CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.ProfileNotFound);
    }

    [Fact]
    public async Task Handle_con_datos_de_contacto_invalidos_devuelve_el_error_del_dominio()
    {
        var patient = GivenPatient(PatientMother.WithAllData(UserId, CurrentProviderId));
        var command = UpdatePatientProfileCommandMother.WithAllData(NewProviderId) with
        {
            PhoneNumber = new string('1', 21)
        };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.PhoneTooLong);
        patient.PhoneNumber.ShouldBe(PatientMother.DefaultPhoneNumber);
        await _insuranceProviderReader.DidNotReceive().IsActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_con_proveedor_sin_poliza_devuelve_el_error_del_dominio()
    {
        var patient = GivenPatient(PatientMother.WithRequiredData(UserId));
        var command = UpdatePatientProfileCommandMother.WithAllData(NewProviderId) with { PolicyNumber = null };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.PolicyNumberRequired);
        patient.InsuranceProviderId.ShouldBeNull();
    }

    private Patient GivenPatient(Patient patient)
    {
        _patientRepository.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);
        return patient;
    }

    private UpdatePatientProfileCommandHandler CreateHandler() =>
        new(_currentUser, _patientRepository, _insuranceProviderReader);
}
