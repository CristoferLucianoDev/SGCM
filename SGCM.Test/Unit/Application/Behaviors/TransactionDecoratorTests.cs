using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Domain.Common;

namespace SGCM.Test.Unit.Application.Behaviors;
using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class TransactionDecoratorTests
{
    private static readonly Error HandlerError = Error.Conflict("Test.Conflict", "Conflicto de prueba.");

    private readonly ICommandHandler<TestCommand, Unit> _inner = Substitute.For<ICommandHandler<TestCommand, Unit>>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ITransaction _transaction = Substitute.For<ITransaction>();
    private readonly TransactionDecorator<TestCommand, Unit> _decorator;

    public TransactionDecoratorTests()
    {
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_transaction);
        _decorator = new TransactionDecorator<TestCommand, Unit>(_inner, _unitOfWork);
    }

    [Fact]
    public async Task Handle_HandlerSucceeds_SavesAndCommits()
    {
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);

        var result = await _decorator.HandleAsync(new TestCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _transaction.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HandlerFails_RollsBackWithoutSaving()
    {
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(HandlerError);

        var result = await _decorator.HandleAsync(new TestCommand(), CancellationToken.None);

        result.Error.ShouldBe(HandlerError);
        await _transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    public sealed record TestCommand : ICommand<Unit>;
}