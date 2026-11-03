using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class LoginCommandHandlerTests
{
    private const string ValidPassword = "Secreta123";
    private const string AttemptKey = "login:paciente@correo.com";
    private const int MaxFailedAttempts = 5;
    private const int EmailWindowMinutes = 15;

    private static readonly LoginCommand Command = new("paciente@correo.com", ValidPassword, "Firefox en Windows", "203.0.113.10");

    private static readonly SessionTokens Tokens = new(
        new AccessToken("access-token", UserMother.Now.AddMinutes(15)),
        "refresh-token",
        UserMother.Now.AddDays(7),
        null);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IAttemptLimiter _attemptLimiter = Substitute.For<IAttemptLimiter>();
    private readonly ITwoFactorStatusReader _twoFactorStatusReader = Substitute.For<ITwoFactorStatusReader>();
    private readonly ISessionService _sessionService = Substitute.For<ISessionService>();
    private readonly User _user = UserMother.Confirmed();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _attemptLimiter.TryRegisterAttempt(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>()).Returns(true);
        _twoFactorStatusReader.IsEnabledAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false);
        _sessionService
            .StartAsync(Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<SessionTokens>.Success(Tokens));
        _passwordHasher.Verify(UserMother.DefaultPasswordHash, ValidPassword).Returns(true);
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(_user);

        var options = Options.Create(new LoginLimitsOptions
        {
            MaxFailedAttemptsPerEmail = MaxFailedAttempts,
            EmailWindowMinutes = EmailWindowMinutes
        });

        _handler = new LoginCommandHandler(
            _userRepository, _passwordHasher, _attemptLimiter, _twoFactorStatusReader, _sessionService, options);
    }

    [Fact]
    public async Task Login_con_credenciales_validas_abre_la_sesion()
    {
        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RequiresTwoFactor.ShouldBeFalse();
        result.Value.Tokens.ShouldBe(Tokens);
        await _sessionService.Received(1).StartAsync(
            _user, Command.DeviceName, Command.IpAddress, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_valido_reinicia_el_contador_de_intentos()
    {
        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _attemptLimiter.Received(1).Reset(AttemptKey);
    }

    [Fact]
    public async Task Login_con_2FA_activo_reinicia_el_contador()
    {
        _twoFactorStatusReader.IsEnabledAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);

        await _handler.HandleAsync(Command, CancellationToken.None);

        _attemptLimiter.Received(1).Reset(AttemptKey);
    }

    [Fact]
    public async Task Login_con_fallo_al_abrir_la_sesion_no_reinicia_el_contador()
    {
        _sessionService
            .StartAsync(Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<SessionTokens>.Failure(UserErrors.AccountInactive));

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.InvalidCredentials);
        _attemptLimiter.DidNotReceive().Reset(Arg.Any<string>());
    }

    [Fact]
    public async Task Login_con_contrasena_null_responde_credenciales_invalidas()
    {
        await ShouldFailWithInvalidCredentialsAsync(Command with { Password = null! });

        _passwordHasher.Received(1).Verify(UserMother.DefaultPasswordHash, string.Empty);
    }

    [Fact]
    public async Task Login_carga_al_usuario_con_la_fila_bloqueada()
    {
        await _handler.HandleAsync(Command, CancellationToken.None);

        await _userRepository.Received(1).GetByEmailForUpdateAsync(UserMother.DefaultEmail, Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_con_correo_inexistente_falla_con_credenciales_invalidas()
    {
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await ShouldFailWithInvalidCredentialsAsync(Command);
    }

    [Fact]
    public async Task Login_con_contrasena_incorrecta_falla_con_credenciales_invalidas()
    {
        await ShouldFailWithInvalidCredentialsAsync(Command with { Password = "OtraClave456" });
    }

    [Fact]
    public async Task Login_con_cuenta_pendiente_falla_con_credenciales_invalidas()
    {
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(UserMother.Pending());

        await ShouldFailWithInvalidCredentialsAsync(Command);
    }

    [Fact]
    public async Task Login_con_cuenta_inactiva_falla_con_credenciales_invalidas()
    {
        _user.Deactivate();

        await ShouldFailWithInvalidCredentialsAsync(Command);
    }

    [Fact]
    public async Task Login_con_correo_inexistente_verifica_contra_el_hash_de_relleno()
    {
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await _handler.HandleAsync(Command, CancellationToken.None);

        _passwordHasher.Received(1).Verify(null, ValidPassword);
    }

    [Fact]
    public async Task Login_con_el_limite_superado_se_rechaza_sin_consultar_al_usuario()
    {
        _attemptLimiter.TryRegisterAttempt(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>()).Returns(false);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TooManyLoginAttempts);
        _userRepository.ReceivedCalls().ShouldBeEmpty();
        _passwordHasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Login_registra_el_intento_con_los_valores_de_la_configuracion()
    {
        await _handler.HandleAsync(Command, CancellationToken.None);

        _attemptLimiter.Received(1).TryRegisterAttempt(
            AttemptKey, MaxFailedAttempts, TimeSpan.FromMinutes(EmailWindowMinutes));
    }

    [Fact]
    public async Task Login_con_2FA_activo_pide_el_segundo_factor_sin_emitir_tokens()
    {
        _twoFactorStatusReader.IsEnabledAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RequiresTwoFactor.ShouldBeTrue();
        result.Value.Tokens.ShouldBeNull();
        await _sessionService.DidNotReceive().StartAsync(
            Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(UserRole.Doctor, PendingStep.ChangeTemporaryPassword, true)]
    [InlineData(UserRole.Doctor, PendingStep.DoctorProfile, true)]
    [InlineData(UserRole.Doctor, null, false)]
    [InlineData(UserRole.Patient, PendingStep.PatientProfile, false)]
    [InlineData(UserRole.Patient, PendingStep.ChangeTemporaryPassword, false)]
    [InlineData(UserRole.Administrator, null, false)]
    [InlineData(UserRole.Administrator, PendingStep.ChangeTemporaryPassword, false)]
    public async Task Primer_acceso_factual_usa_rol_autenticado_y_paso_real(UserRole role, PendingStep? step, bool expected)
    {
        var user = UserMother.WithRole(role, mustChangePassword: step == PendingStep.ChangeTemporaryPassword);
        var tokens = Tokens with { PendingStep = step };
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(user);
        _sessionService.StartAsync(user, Command.DeviceName, Command.IpAddress, CancellationToken.None).Returns(tokens);
        var result = await _handler.HandleAsync(Command, CancellationToken.None);
        result.Value.HasPendingDoctorFirstAccess.ShouldBe(expected);
        result.Value.RequiresTwoFactor.ShouldBeFalse();
        result.Value.Tokens.ShouldBeSameAs(tokens);
        await _sessionService.Received(1).StartAsync(user, Command.DeviceName, Command.IpAddress, CancellationToken.None);
        _userRepository.ReceivedCalls().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Primer_acceso_factual_no_se_confirma_antes_del_segundo_factor_del_Doctor()
    {
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(UserMother.WithRole(UserRole.Doctor, mustChangePassword: true));
        _twoFactorStatusReader.IsEnabledAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        var result = await _handler.HandleAsync(Command, CancellationToken.None);
        result.Value.HasPendingDoctorFirstAccess.ShouldBeFalse();
        result.Value.RequiresTwoFactor.ShouldBeTrue();
        result.Value.Tokens.ShouldBeNull();
        _sessionService.ReceivedCalls().ShouldBeEmpty();
    }

    private async Task ShouldFailWithInvalidCredentialsAsync(LoginCommand command)
    {
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.InvalidCredentials);
        await _sessionService.DidNotReceive().StartAsync(
            Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _attemptLimiter.DidNotReceive().Reset(Arg.Any<string>());
    }
}
