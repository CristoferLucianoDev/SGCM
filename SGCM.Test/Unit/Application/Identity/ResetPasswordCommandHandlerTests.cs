using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity.Commands.ResetPassword;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ResetPasswordCommandHandlerTests
{
    private const string ResetRawToken = "raw-reset";
    private const string ResetTokenHash = "hash-reset";
    private const string NewPassword = "NuevaClave123";
    private const string NewPasswordHash = "hash-nueva";
    private const string LoginAttemptKey = "login:paciente@correo.com";

    // Vigencia del enlace de recuperación que fija el PBI y de la sesión que fija la arquitectura.
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    private const int MaxSessions = 5;

    private static readonly ResetPasswordCommand Command = new(ResetRawToken, NewPassword);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IAttemptLimiter _attemptLimiter = Substitute.For<IAttemptLimiter>();
    private readonly FakeDateTimeProvider _dateTimeProvider = new();
    private readonly User _user = UserMother.Confirmed();
    private readonly ResetPasswordCommandHandler _handler;

    public ResetPasswordCommandHandlerTests()
    {
        _user.IssueToken(UserTokenType.PasswordReset, ResetRawToken, ResetTokenHash, UserMother.Now, ResetLifetime);
        _user.StartSession("hash-sesion", "Chrome en Windows", "203.0.113.10", UserMother.Now, SessionLifetime, MaxSessions);
        _dateTimeProvider.UtcNow = UserMother.Now.AddMinutes(1);

        _tokenGenerator.Hash(Arg.Any<string>()).Returns(ResetTokenHash);
        _passwordHasher.Hash(Arg.Any<Password>()).Returns(NewPasswordHash);
        _userRepository
            .GetByTokenHashForUpdateAsync(Arg.Any<UserTokenType>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_user);

        _handler = new ResetPasswordCommandHandler(
            _userRepository, _tokenGenerator, _passwordHasher, _dateTimeProvider, _attemptLimiter);
    }

    [Fact]
    public async Task Handle_ValidToken_ResetsPasswordAndRevokesSessions()
    {
        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _passwordHasher.Received(1).Hash(Arg.Is<Password>(password => password.Value == NewPassword));
        _user.PasswordHash.ShouldBe(NewPasswordHash);
        _user.Tokens.Single(token => token.TokenHash == ResetTokenHash).UsedAt.ShouldBe(_dateTimeProvider.UtcNow);
        _user.Sessions.ShouldAllBe(session => session.RevocationReason == SessionRevocationReason.PasswordReset);
        await _userRepository.Received(1).GetByTokenHashForUpdateAsync(
            UserTokenType.PasswordReset, ResetTokenHash, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidToken_ResetsLoginAttempts()
    {
        await _handler.HandleAsync(Command, CancellationToken.None);

        _attemptLimiter.Received(1).Reset(LoginAttemptKey);
    }

    [Fact]
    public async Task Handle_UnknownToken_FailsWithoutHashingThePassword()
    {
        _userRepository
            .GetByTokenHashForUpdateAsync(Arg.Any<UserTokenType>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        _passwordHasher.DidNotReceive().Hash(Arg.Any<Password>());
    }

    [Fact]
    public async Task Handle_ExpiredToken_FailsWithoutResettingLoginAttempts()
    {
        _dateTimeProvider.UtcNow = UserMother.Now + ResetLifetime;

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TokenExpired);
        _attemptLimiter.DidNotReceive().Reset(Arg.Any<string>());
        _user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
    }

    [Fact]
    public async Task Handle_ExpiredToken_DoesNotHashThePassword()
    {
        _dateTimeProvider.UtcNow = UserMother.Now + ResetLifetime;

        await _handler.HandleAsync(Command, CancellationToken.None);

        _passwordHasher.DidNotReceive().Hash(Arg.Any<Password>());
    }

    [Fact]
    public async Task Handle_UsedToken_FailsWithoutHashingThePassword()
    {
        _user.ResetPassword(ResetTokenHash, "hash-anterior", UserMother.Now).IsSuccess.ShouldBeTrue();

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        _passwordHasher.DidNotReceive().Hash(Arg.Any<Password>());
    }

    [Fact]
    public async Task Handle_InactiveAccount_FailsWithoutResettingLoginAttempts()
    {
        _user.Deactivate();

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.AccountInactive);
        _attemptLimiter.DidNotReceive().Reset(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_BlankToken_FailsWithoutQuerying()
    {
        var result = await _handler.HandleAsync(Command with { Token = "   " }, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        _tokenGenerator.DidNotReceive().Hash(Arg.Any<string>());
        _userRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ShortPassword_FailsWithoutQuerying()
    {
        var shortPassword = new string('a', Password.MinLength - 1);

        var result = await _handler.HandleAsync(Command with { NewPassword = shortPassword }, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.PasswordTooShort);
        _userRepository.ReceivedCalls().ShouldBeEmpty();
    }
}
