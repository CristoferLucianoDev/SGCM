using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class RegisterPatientAccountCommandHandlerTests
{
    private const string NewPasswordHash = "hash-nuevo";
    private const int ConfirmationMinutes = 15;

    private static readonly SecureToken NewToken = new("raw-nuevo", "hash-token-nuevo");
    private static readonly RegisterPatientAccountCommand Command = new("paciente@correo.com", "Secreta123");

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly FakeDateTimeProvider _dateTimeProvider = new();
    private readonly RegisterPatientAccountCommandHandler _handler;

    public RegisterPatientAccountCommandHandlerTests()
    {
        _passwordHasher.Hash(Arg.Any<Password>()).Returns(NewPasswordHash);
        _tokenGenerator.Generate().Returns(NewToken);

        var options = Options.Create(new AccountsOptions { EmailConfirmationTokenMinutes = ConfirmationMinutes });

        _handler = new RegisterPatientAccountCommandHandler(
            _userRepository, _passwordHasher, _tokenGenerator, _dateTimeProvider, options);
    }

    [Fact]
    public async Task Handle_NewEmail_AddsPendingPatientWithConfirmationToken()
    {
        User? added = null;
        _userRepository.Add(Arg.Do<User>(user => added = user));

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        added.ShouldNotBeNull();
        added.Role.ShouldBe(UserRole.Patient);
        added.IsEmailConfirmed.ShouldBeFalse();
        added.PasswordHash.ShouldBe(NewPasswordHash);

        var token = added.Tokens.ShouldHaveSingleItem();
        token.TokenHash.ShouldBe(NewToken.Hash);
        token.ExpiresAt.ShouldBe(UserMother.Now.AddMinutes(ConfirmationMinutes));

        var issued = added.DomainEvents.OfType<UserTokenIssuedEvent>().ShouldHaveSingleItem();
        issued.RawToken.ShouldBe(NewToken.Value);
    }

    [Fact]
    public async Task Handle_EmailWithSpacesAndUppercase_LooksUpNormalizedEmail()
    {
        var command = Command with { Email = "  PACIENTE@Correo.com " };

        await _handler.HandleAsync(command, CancellationToken.None);

        await _userRepository.Received(1).GetByEmailAsync(
            Email.Create("paciente@correo.com").Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingEmail_ReplacesRegistrationWithoutAddingUser()
    {
        var pending = UserMother.Pending();
        var previousToken = pending.Tokens.Single();
        _userRepository.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(pending);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _userRepository.DidNotReceive().Add(Arg.Any<User>());
        pending.PasswordHash.ShouldBe(NewPasswordHash);
        previousToken.IsValidAt(UserMother.Now).ShouldBeFalse();
        pending.Tokens.Count(token => token.IsValidAt(UserMother.Now)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ConfirmedEmail_FailsWithEmailAlreadyInUse()
    {
        _userRepository.GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(UserMother.Confirmed());

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.EmailAlreadyInUse);
        _userRepository.DidNotReceive().Add(Arg.Any<User>());
        _passwordHasher.DidNotReceive().Hash(Arg.Any<Password>());
    }

    [Fact]
    public async Task Handle_InvalidEmail_FailsWithoutQueryingRepository()
    {
        var result = await _handler.HandleAsync(Command with { Email = "no-es-un-correo" }, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }
}