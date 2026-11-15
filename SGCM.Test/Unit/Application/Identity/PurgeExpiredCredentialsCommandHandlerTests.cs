using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Identity.Commands.PurgeExpiredCredentials;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Fakes;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class PurgeExpiredCredentialsCommandHandlerTests
{
    private const int RetentionDays = 30;

    private static readonly PurgeExpiredCredentialsCommand Command = new();

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly FakeDateTimeProvider _dateTimeProvider = new();
    private readonly PurgeExpiredCredentialsCommandHandler _handler;

    public PurgeExpiredCredentialsCommandHandlerTests()
    {
        _handler = new PurgeExpiredCredentialsCommandHandler(
            _userRepository,
            _dateTimeProvider,
            Options.Create(new AccountsOptions { CredentialRetentionDays = RetentionDays }));
    }

    [Fact]
    public async Task Handle_ReturnsTheNumberOfDeletedCredentials()
    {
        _userRepository
            .DeleteEndedCredentialsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(4);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(4);
    }

    [Fact]
    public async Task Handle_DeletesUsingTheCurrentTimeMinusTheRetentionPeriod()
    {
        await _handler.HandleAsync(Command, CancellationToken.None);

        await _userRepository.Received(1).DeleteEndedCredentialsAsync(
            _dateTimeProvider.UtcNow - TimeSpan.FromDays(RetentionDays), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NothingToDelete_ReturnsZero()
    {
        _userRepository
            .DeleteEndedCredentialsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }
}
