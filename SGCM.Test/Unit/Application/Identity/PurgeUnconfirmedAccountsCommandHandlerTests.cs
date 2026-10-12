using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Identity.Commands.PurgeUnconfirmedAccounts;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Fakes;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class PurgeUnconfirmedAccountsCommandHandlerTests
{
    private static readonly PurgeUnconfirmedAccountsCommand Command = new();

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly FakeDateTimeProvider _dateTimeProvider = new();
    private readonly PurgeUnconfirmedAccountsCommandHandler _handler;

    public PurgeUnconfirmedAccountsCommandHandlerTests()
    {
        _handler = new PurgeUnconfirmedAccountsCommandHandler(_userRepository, _dateTimeProvider);
    }

    [Fact]
    public async Task Handle_ReturnsTheNumberOfDeletedAccounts()
    {
        _userRepository
            .DeleteExpiredPendingRegistrationsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(3);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_DeletesUsingTheCurrentTime()
    {
        await _handler.HandleAsync(Command, CancellationToken.None);

        await _userRepository.Received(1).DeleteExpiredPendingRegistrationsAsync(
            _dateTimeProvider.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NothingToDelete_ReturnsZero()
    {
        _userRepository
            .DeleteExpiredPendingRegistrationsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }
}
