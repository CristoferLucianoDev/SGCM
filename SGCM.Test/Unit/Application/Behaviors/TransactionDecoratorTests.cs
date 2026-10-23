using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_PersistenceConflict_RollsBackWithoutCommit(bool duringHandler)
    {
        var original = new InvalidOperationException("Causa de prueba.");
        var conflict = new PersistenceConflictException(SpecialtyErrors.NameAlreadyInUse, original);
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => duringHandler ? Task.FromException<Result<Unit>>(conflict) : Task.FromResult<Result<Unit>>(Unit.Value));
        if (!duringHandler)
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(conflict));
        }

        var result = await _decorator.HandleAsync(new TestCommand(), CancellationToken.None);

        result.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
        await _transaction.Received(1).RollbackAsync(CancellationToken.None);
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(duringHandler ? 0 : 1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_UnexpectedException_PropagatesAndDisposesTransaction(bool duringHandler)
    {
        var original = new InvalidOperationException("Error inesperado.");
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => duringHandler ? Task.FromException<Result<Unit>>(original) : Task.FromResult<Result<Unit>>(Unit.Value));
        if (!duringHandler)
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(original));
        }

        var actual = await Should.ThrowAsync<InvalidOperationException>(() =>
            _decorator.HandleAsync(new TestCommand(), CancellationToken.None));

        actual.ShouldBeSameAs(original);
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Handle_PersistenceConflict_RollsBackEvenIfTheRequestWasCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        var conflict = new PersistenceConflictException(SpecialtyErrors.NameAlreadyInUse, new InvalidOperationException());
        _inner.HandleAsync(Arg.Any<TestCommand>(), cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<Result<Unit>>(conflict);
        });

        var result = await _decorator.HandleAsync(new TestCommand(), cancellation.Token);

        result.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
        await _transaction.Received(1).RollbackAsync(CancellationToken.None);
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CommitFailure_IsNotConvertedToAResult()
    {
        var original = new PersistenceConflictException(SpecialtyErrors.NameAlreadyInUse, new InvalidOperationException());
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _transaction.CommitAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(original));

        var actual = await Should.ThrowAsync<PersistenceConflictException>(() =>
            _decorator.HandleAsync(new TestCommand(), CancellationToken.None));

        actual.ShouldBeSameAs(original);
        await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Handle_Cancellation_PropagatesWithoutCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var original = new OperationCanceledException(cancellation.Token);
        _inner.HandleAsync(Arg.Any<TestCommand>(), cancellation.Token).Returns(Task.FromException<Result<Unit>>(original));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _decorator.HandleAsync(new TestCommand(), cancellation.Token));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    public sealed record TestCommand : ICommand<Unit>;
}
