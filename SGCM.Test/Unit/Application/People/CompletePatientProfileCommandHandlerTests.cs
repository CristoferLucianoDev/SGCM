using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.CompletePatientProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public class CompletePatientProfileCommandHandlerTests
{
    private const int UserId = 7;
    private const int InsuranceProviderId = 3;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPatientRepository _patientRepository = Substitute.For<IPatientRepository>();
    private readonly IInsuranceProviderReader _insuranceProviderReader = Substitute.For<IInsuranceProviderReader>();
    private readonly ISessionService _sessionService = Substitute.For<ISessionService>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly User _user = UserMother.Confirmed();

    private readonly SessionTokens _tokens = new(
        new AccessToken("access-token", UserMother.Now.AddMinutes(15)),
        "refresh-token",
        UserMother.Now.AddDays(7),
        PendingStep: null);

    public CompletePatientProfileCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _userRepository.GetByIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns(_user);
        _insuranceProviderReader.IsActiveAsync(InsuranceProviderId, Arg.Any<CancellationToken>()).Returns(true);
        _sessionService.StartAsync(_user, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_tokens);
    }

    [Fact]
    public async Task Handle_crea_el_perfil_del_usuario_autenticado_y_devuelve_los_tokens_nuevos()
    {
        using var cancellation = new CancellationTokenSource();
        var command = CompletePatientProfileCommandMother.WithRequiredData();

        var result = await CreateHandler().HandleAsync(command, cancellation.Token);

        result.Value.ShouldBe(_tokens);
        Received.InOrder(() =>
        {
            _userRepository.GetByIdForUpdateAsync(UserId, cancellation.Token);
            _patientRepository.ExistsByUserIdAsync(UserId, cancellation.Token);
            _patientRepository.Add(Arg.Is<Patient>(patient =>
                patient.UserId == UserId
                && patient.FirstName == PatientMother.DefaultFirstName
                && patient.LastName == PatientMother.DefaultLastName
                && patient.BirthDate == PatientMother.DefaultBirthDate
                && patient.PhoneNumber == PatientMother.DefaultPhoneNumber
                && patient.CreatedAt == _clock.UtcNow));
            _sessionService.StartAsync(
                _user,
                CompletePatientProfileCommandMother.DefaultDeviceName,
                CompletePatientProfileCommandMother.DefaultIpAddress,
                cancellation.Token);
        });
    }

    [Fact]
    public async Task Handle_sin_seguro_no_consulta_el_proveedor()
    {
        await CreateHandler().HandleAsync(CompletePatientProfileCommandMother.WithRequiredData(), CancellationToken.None);

        await _insuranceProviderReader.DidNotReceive().IsActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_con_proveedor_activo_guarda_el_seguro_y_el_contacto_de_emergencia()
    {
        var command = CompletePatientProfileCommandMother.WithAllData(InsuranceProviderId);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _patientRepository.Received(1).Add(Arg.Is<Patient>(patient =>
            patient.InsuranceProviderId == InsuranceProviderId
            && patient.PolicyNumber == PatientMother.DefaultPolicyNumber
            && patient.EmergencyContact != null
            && patient.EmergencyContact.Name == PatientMother.DefaultEmergencyContactName));
    }

    [Fact]
    public async Task Handle_usuario_inexistente_responde_credenciales_invalidas_sin_crear_el_perfil()
    {
        _userRepository.GetByIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().HandleAsync(
            CompletePatientProfileCommandMother.WithRequiredData(), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.InvalidCredentials);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task Handle_perfil_existente_responde_conflicto_sin_crear_otro()
    {
        _patientRepository.ExistsByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().HandleAsync(
            CompletePatientProfileCommandMother.WithRequiredData(), CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.ProfileAlreadyExists);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task Handle_fecha_de_nacimiento_futura_devuelve_el_error_del_dominio()
    {
        var tomorrow = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).AddDays(1);
        var command = CompletePatientProfileCommandMother.WithRequiredData() with { BirthDate = tomorrow };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.InvalidBirthDate);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task Handle_proveedor_inexistente_o_inactivo_responde_proveedor_invalido()
    {
        _insuranceProviderReader.IsActiveAsync(InsuranceProviderId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().HandleAsync(
            CompletePatientProfileCommandMother.WithAllData(InsuranceProviderId), CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.InvalidInsuranceProvider);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task Handle_devuelve_el_error_de_la_sesion_para_que_la_transaccion_se_revierta()
    {
        _sessionService.StartAsync(_user, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<SessionTokens>.Failure(UserErrors.AccountInactive));

        var result = await CreateHandler().HandleAsync(
            CompletePatientProfileCommandMother.WithRequiredData(), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.AccountInactive);
    }

    private CompletePatientProfileCommandHandler CreateHandler() => new(
        _currentUser, _userRepository, _patientRepository, _insuranceProviderReader, _sessionService, _clock);

    private async Task AssertNothingCreatedAsync()
    {
        _patientRepository.DidNotReceive().Add(Arg.Any<Patient>());
        await _sessionService.DidNotReceive()
            .StartAsync(Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
