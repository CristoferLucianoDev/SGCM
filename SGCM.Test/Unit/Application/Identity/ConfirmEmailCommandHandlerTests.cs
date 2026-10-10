using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.ConfirmEmail;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ConfirmEmailCommandHandlerTests
{
    private static readonly ConfirmEmailCommand Command = new(UserMother.DefaultRawToken);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly FakeDateTimeProvider _dateTimeProvider = new();
    private readonly User _user = UserMother.Pending();
    private readonly ConfirmEmailCommandHandler _handler;

    public ConfirmEmailCommandHandlerTests()
    {
        _tokenGenerator.Hash(Arg.Any<string>()).Returns(UserMother.DefaultTokenHash);
        _userRepository
            .GetByTokenHashAsync(Arg.Any<UserTokenType>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_user);

        _handler = new ConfirmEmailCommandHandler(_userRepository, _tokenGenerator, _dateTimeProvider);
    }

    [Fact]
    public async Task Handle_ValidToken_ConfirmsAccount()
    {
        _dateTimeProvider.UtcNow = UserMother.Now.AddMinutes(1);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.IsEmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_SearchesByEmailConfirmationTypeAndComputedHash()
    {
        _dateTimeProvider.UtcNow = UserMother.Now.AddMinutes(1);

        await _handler.HandleAsync(Command, CancellationToken.None);

        await _userRepository.Received(1).GetByTokenHashAsync(
            UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownToken_FailsWithTokenInvalid()
    {
        _userRepository
            .GetByTokenHashAsync(Arg.Any<UserTokenType>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TokenInvalid);
    }

    [Fact]
    public async Task Handle_ExpiredRegistration_FailsWithRegistrationExpired()
    {
        _dateTimeProvider.UtcNow = UserMother.Now + UserMother.ConfirmationLifetime;

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.RegistrationExpired);
        _user.IsEmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_BlankToken_FailsWithoutHashingOrQuerying()
    {
        var result = await _handler.HandleAsync(new ConfirmEmailCommand("   "), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TokenInvalid);
        _tokenGenerator.DidNotReceive().Hash(Arg.Any<string>());
        await _userRepository.DidNotReceive().GetByTokenHashAsync(
            Arg.Any<UserTokenType>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
