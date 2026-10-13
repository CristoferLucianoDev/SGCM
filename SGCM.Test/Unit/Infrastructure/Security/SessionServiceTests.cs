using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Infrastructure.Security;

/// <summary>
/// Pruebas de la apertura de sesiones en <see cref="SessionService"/>.
/// </summary>
public sealed class SessionServiceTests
{
    private const int RefreshTokenSizeInBytes = 64;
    private const int RefreshTokenDays = 7;
    private const int MaxActiveSessions = 5;
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";

    private static readonly SecureToken RefreshToken = new("refresh-raw", "refresh-hash");
    private static readonly AccessToken IssuedAccessToken = new("access-token", UserMother.Now.AddMinutes(15));

    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly SessionService _service;

    public SessionServiceTests()
    {
        _tokenGenerator.Generate(RefreshTokenSizeInBytes).Returns(RefreshToken);
        _tokenService.CreateAccessToken(Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<int>()).Returns(IssuedAccessToken);

        var options = Options.Create(new JwtOptions
        {
            RefreshTokenDays = RefreshTokenDays,
            MaxActiveSessionsPerUser = MaxActiveSessions
        });

        _service = new SessionService(_tokenGenerator, _tokenService, _unitOfWork, _clock, options);
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
            _tokenService.CreateAccessToken(Arg.Is(user.Id), user.Role, Arg.Any<int>());
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
        _tokenService.DidNotReceive().CreateAccessToken(Arg.Any<int>(), Arg.Any<UserRole>(), Arg.Any<int>());
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

    private static User ConfirmedUser()
    {
        var user = UserMother.Pending();
        user.ConfirmEmail(UserMother.DefaultTokenHash, UserMother.Now);
        return user;
    }
}
