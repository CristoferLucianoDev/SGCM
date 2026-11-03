using Microsoft.Extensions.Options;
using NSubstitute;
using Serilog;
using Shouldly;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Unit.Domain.Identity.Users;

namespace SGCM.Test.Unit.Infrastructure.Security;

/// <summary>
/// Pruebas de la apertura, la renovación y el cierre de sesiones en <see cref="SessionService"/>.
/// </summary>
public sealed class SessionServiceTests
{
    private const int RefreshTokenSizeInBytes = 64;
    private const int RefreshTokenDays = 7;
    private const int MaxActiveSessions = 5;
    private const int ReuseGraceSeconds = 30;
    private const int UserId = 42;
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";
    private const string PresentedToken = "refresh-presentado";

    private static readonly SecureToken RefreshToken = new("refresh-raw", "refresh-hash");
    private static readonly SecureToken RotatedToken = new("rotated-raw", "rotated-hash");
    private static readonly AccessToken IssuedAccessToken = new("access-token", UserMother.Now.AddMinutes(15));

    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IUserProfileReader _profileReader = Substitute.For<IUserProfileReader>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly SessionService _service;

    public SessionServiceTests()
    {
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(RefreshToken);
        _tokenService
            .CreateAccessToken(Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<int>(), Arg.Any<PendingStep?>())
            .Returns(IssuedAccessToken);
        _profileReader
            .HasProfileAsync(Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _tokenGenerator.Hash(PresentedToken).Returns(RefreshToken.Hash);
        _logger.ForContext<SessionService>().Returns(_logger);

        var options = Options.Create(new JwtOptions
        {
            RefreshTokenDays = RefreshTokenDays,
            MaxActiveSessionsPerUser = MaxActiveSessions,
            RefreshReuseGraceSeconds = ReuseGraceSeconds
        });

        _service = new SessionService(
            _tokenGenerator, _tokenService, _unitOfWork, _profileReader, _userRepository, _clock, options, _logger);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenewCurrentAsync_rota_la_misma_sesion_y_recalcula_el_paso(bool hasProfile)
    {
        var data = new PasswordSessionScenario();
        data.Owner.ChangeTemporaryPassword("nuevo-hash", data.Current.Id, _clock.UtcNow).IsSuccess.ShouldBeTrue();
        var otherBefore = data.Owner.Sessions.Where(s => s.Id != data.Current.Id)
            .Select(PasswordSessionScenario.SessionState.From).ToArray();
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);
        var count = data.Owner.Sessions.Count;
        var previous = data.Current.RefreshTokenHash;
        var device = data.Current.DeviceName;
        var ip = data.Current.IpAddress;
        _profileReader.HasProfileAsync(data.Owner.Id, UserRole.Doctor, Arg.Any<CancellationToken>()).Returns(hasProfile);

        var result = await _service.RenewCurrentAsync(data.Owner, data.Current.Id, _clock.UtcNow, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RefreshToken.ShouldBe(RefreshToken.Value);
        result.Value.AccessToken.ShouldBe(IssuedAccessToken);
        result.Value.PendingStep.ShouldBe(hasProfile ? null : PendingStep.DoctorProfile);
        result.Value.RefreshTokenExpiresAt.ShouldBe(_clock.UtcNow.AddDays(RefreshTokenDays));
        data.Owner.Sessions.Count.ShouldBe(count);
        data.Current.Id.ShouldBe(101);
        data.Current.RefreshTokenHash.ShouldBe(RefreshToken.Hash);
        data.Current.PreviousRefreshTokenHash.ShouldBe(previous);
        data.Current.LastUsedAt.ShouldBe(_clock.UtcNow);
        data.Current.DeviceName.ShouldBe(device);
        data.Current.IpAddress.ShouldBe(ip);
        data.Current.RevokedAt.ShouldBeNull();
        data.Owner.Sessions.Where(s => s.Id != data.Current.Id)
            .Select(PasswordSessionScenario.SessionState.From).ShouldBe(otherBefore);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
        _tokenService.Received(1).CreateAccessToken(data.Owner.Id, UserRole.Doctor, data.Current.Id,
            hasProfile ? null : PendingStep.DoctorProfile);
        _userRepository.ReceivedCalls().ShouldBeEmpty();
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("foreign")]
    [InlineData("revocada")]
    [InlineData("expirada")]
    [InlineData("limite")]
    public async Task RenewCurrentAsync_rechaza_sin_generar_tokens_ni_mutar(string kind)
    {
        var data = new PasswordSessionScenario();
        if (kind == "inactive") data.Owner.Deactivate();
        var id = kind switch
        {
            "missing" => 999,
            "zero" => 0,
            "foreign" => data.ForeignCurrent.Id,
            "revocada" or "expirada" or "limite" => data.InactiveSession(kind).Id,
            _ => data.Current.Id
        };
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        (await _service.RenewCurrentAsync(data.Owner, id, _clock.UtcNow, CancellationToken.None))
            .Error.ShouldBe(UserErrors.SessionInvalid);
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        _tokenGenerator.ReceivedCalls().ShouldBeEmpty();
        _tokenService.ReceivedCalls().ShouldBeEmpty();
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
    }

    public static TheoryData<string?, string?> DeviceNames => new()
    {
        { "  Chrome  ", "Chrome" },
        { "   ", null },
        { null, null },
        { new string('d', 200), new string('d', UserSession.MaxDeviceNameLength) }
    };

    [Fact]
    public async Task StartAsync_abre_la_sesion_y_devuelve_los_tokens()
    {
        var user = ConfirmedUser();

        var result = await _service.StartAsync(user, DeviceName, IpAddress, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RefreshToken.ShouldBe(RefreshToken.Value);
        result.Value.RefreshTokenExpiresAt.ShouldBe(_clock.UtcNow.AddDays(RefreshTokenDays));
        result.Value.AccessToken.ShouldBe(IssuedAccessToken);
        user.Sessions.ShouldHaveSingleItem().RefreshTokenHash.ShouldBe(RefreshToken.Hash);
    }

    [Fact]
    public async Task StartAsync_guarda_antes_de_emitir_el_access_token()
    {
        var user = ConfirmedUser();

        await _service.StartAsync(user, DeviceName, IpAddress, CancellationToken.None);

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _tokenService.CreateAccessToken(Arg.Is(user.Id), Arg.Is(user.Role), Arg.Any<int>(), Arg.Any<PendingStep?>());
        });
    }

    [Fact]
    public async Task StartAsync_pide_un_refresh_token_de_64_bytes()
    {
        await _service.StartAsync(ConfirmedUser(), DeviceName, IpAddress, CancellationToken.None);

        _tokenGenerator.Received(1).Generate(RefreshTokenSizeInBytes);
    }

    [Fact]
    public async Task StartAsync_con_cuenta_inactiva_devuelve_el_error_sin_guardar()
    {
        var user = ConfirmedUser();
        user.Deactivate();

        var result = await _service.StartAsync(user, DeviceName, IpAddress, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.AccountInactive);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _tokenService.DidNotReceive()
            .CreateAccessToken(Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<int>(), Arg.Any<PendingStep?>());
    }

    [Theory]
    [MemberData(nameof(DeviceNames))]
    public async Task StartAsync_normaliza_el_dispositivo(string? deviceName, string? expected)
    {
        var user = ConfirmedUser();

        await _service.StartAsync(user, deviceName, IpAddress, CancellationToken.None);

        user.Sessions.ShouldHaveSingleItem().DeviceName.ShouldBe(expected);
    }

    [Theory]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("no-es-una-ip", null)]
    [InlineData(null, null)]
    public async Task StartAsync_normaliza_la_ip(string? ipAddress, string? expected)
    {
        var user = ConfirmedUser();

        await _service.StartAsync(user, DeviceName, ipAddress, CancellationToken.None);

        user.Sessions.ShouldHaveSingleItem().IpAddress.ShouldBe(expected);
    }

    [Fact]
    public async Task StartAsync_con_perfil_no_lleva_paso_pendiente()
    {
        var result = await _service.StartAsync(ConfirmedUser(), DeviceName, IpAddress, CancellationToken.None);

        result.Value.PendingStep.ShouldBeNull();
        _tokenService.Received(1).CreateAccessToken(
            Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<int>(), Arg.Is<PendingStep?>(step => step == null));
    }

    [Fact]
    public async Task StartAsync_de_un_paciente_sin_perfil_lleva_su_perfil_como_paso()
    {
        _profileReader
            .HasProfileAsync(Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _service.StartAsync(ConfirmedUser(), DeviceName, IpAddress, CancellationToken.None);

        result.Value.PendingStep.ShouldBe(PendingStep.PatientProfile);
        _tokenService.Received(1).CreateAccessToken(
            Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<int>(), Arg.Is<PendingStep?>(PendingStep.PatientProfile));
    }

    [Fact]
    public async Task StartAsync_consulta_el_perfil_del_usuario_y_su_rol()
    {
        var user = ConfirmedUser();

        await _service.StartAsync(user, DeviceName, IpAddress, CancellationToken.None);

        await _profileReader.Received(1)
            .HasProfileAsync(Arg.Is(user.Id), Arg.Is(user.Role), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_rota_la_sesion_y_devuelve_tokens_nuevos_sin_guardar()
    {
        var (user, session) = await UserWithSessionAsync();
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(RotatedToken);

        var result = await _service.RefreshAsync(PresentedToken, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RefreshToken.ShouldBe(RotatedToken.Value);
        result.Value.RefreshTokenExpiresAt.ShouldBe(_clock.UtcNow.AddDays(RefreshTokenDays));
        result.Value.AccessToken.ShouldBe(IssuedAccessToken);
        session.RefreshTokenHash.ShouldBe(RotatedToken.Hash);
        session.PreviousRefreshTokenHash.ShouldBe(RefreshToken.Hash);
        user.Sessions.ShouldHaveSingleItem();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_recalcula_el_paso_pendiente()
    {
        await UserWithSessionAsync();
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(RotatedToken);
        _profileReader
            .HasProfileAsync(Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _service.RefreshAsync(PresentedToken, CancellationToken.None);

        result.Value.PendingStep.ShouldBe(PendingStep.PatientProfile);
        _tokenService.Received(1).CreateAccessToken(
            Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<int>(), Arg.Is<PendingStep?>(PendingStep.PatientProfile));
    }

    [Fact]
    public async Task RefreshAsync_con_usuario_no_encontrado_devuelve_SessionInvalid()
    {
        var result = await _service.RefreshAsync(PresentedToken, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        _tokenGenerator.DidNotReceive().Generate(Arg.Any<int>());
        _logger.DidNotReceiveWithAnyArgs().Warning(default!, default(int));
    }

    [Fact]
    public async Task RefreshAsync_con_token_reutilizado_revoca_avisa_y_devuelve_el_error()
    {
        var (user, session) = await UserWithSessionAsync();
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(RotatedToken);
        await _service.RefreshAsync(PresentedToken, CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(ReuseGraceSeconds + 1);
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(new SecureToken("otro-raw", "otro-hash"));

        var result = await _service.RefreshAsync(PresentedToken, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.RefreshTokenReused);
        session.RevocationReason.ShouldBe(SessionRevocationReason.TokenReuse);
        _logger.Received(1).Warning(Arg.Any<string>(), user.Id);
    }

    [Fact]
    public async Task RefreshAsync_dentro_del_margen_devuelve_SessionInvalid_sin_revocar_ni_avisar()
    {
        var (_, session) = await UserWithSessionAsync();
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(RotatedToken);
        await _service.RefreshAsync(PresentedToken, CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(ReuseGraceSeconds);
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(new SecureToken("otro-raw", "otro-hash"));

        var result = await _service.RefreshAsync(PresentedToken, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        session.RevokedAt.ShouldBeNull();
        _logger.DidNotReceiveWithAnyArgs().Warning(default!, default(int));
    }

    [Fact]
    public async Task EndAsync_revoca_la_sesion_con_Logout_sin_guardar()
    {
        var (_, session) = await UserWithSessionAsync();

        await _service.EndAsync(PresentedToken, CancellationToken.None);

        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        session.RevokedAt.ShouldBe(_clock.UtcNow);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _logger.DidNotReceiveWithAnyArgs().Warning(default!, default(int));
    }

    [Fact]
    public async Task EndAsync_con_el_token_anterior_cierra_la_sesion_y_avisa_con_el_id_del_usuario()
    {
        var (user, session) = await UserWithSessionAsync();
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(RotatedToken);
        await _service.RefreshAsync(PresentedToken, CancellationToken.None);

        await _service.EndAsync(PresentedToken, CancellationToken.None);

        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        _logger.Received(1).Warning(Arg.Any<string>(), user.Id);
    }

    [Fact]
    public async Task EndAsync_repetido_no_avisa()
    {
        var (_, session) = await UserWithSessionAsync();
        await _service.EndAsync(PresentedToken, CancellationToken.None);

        await _service.EndAsync(PresentedToken, CancellationToken.None);

        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        _logger.DidNotReceiveWithAnyArgs().Warning(default!, default(int));
    }

    [Fact]
    public async Task EndAsync_con_token_desconocido_no_falla()
    {
        await Should.NotThrowAsync(() => _service.EndAsync(PresentedToken, CancellationToken.None));

        await _userRepository.Received(1)
            .GetBySessionRefreshTokenHashForUpdateAsync(RefreshToken.Hash, Arg.Any<CancellationToken>());
        _logger.DidNotReceiveWithAnyArgs().Warning(default!, default(int));
    }

    // Abre una sesión cuyo hash es el del token presentado y deja al repositorio devolviendo a su
    // dueño. Olvida el guardado de la apertura, para que las pruebas vean solo el de la operación.
    private async Task<(User User, UserSession Session)> UserWithSessionAsync()
    {
        var user = ConfirmedUser();

        // Un id distinto de cero y del de la sesión, para que las pruebas del aviso detecten otro valor.
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(user, UserId);
        await _service.StartAsync(user, DeviceName, IpAddress, CancellationToken.None);
        _unitOfWork.ClearReceivedCalls();
        _userRepository
            .GetBySessionRefreshTokenHashForUpdateAsync(RefreshToken.Hash, Arg.Any<CancellationToken>())
            .Returns(user);

        return (user, user.Sessions.Single());
    }

    private static User ConfirmedUser()
    {
        var user = UserMother.Pending();
        user.ConfirmEmail(UserMother.DefaultTokenHash, UserMother.Now);
        return user;
    }
}
