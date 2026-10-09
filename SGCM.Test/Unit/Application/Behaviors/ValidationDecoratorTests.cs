using FluentValidation;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Domain.Common;

namespace SGCM.Test.Unit.Application.Behaviors;
using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class ValidationDecoratorTests
{
    private const string ErrorCode = "Test.Invalid";

    private readonly ICommandHandler<TestCommand, Unit> _inner = Substitute.For<ICommandHandler<TestCommand, Unit>>();
    private readonly ValidationDecorator<TestCommand, Unit> _decorator;

    public ValidationDecoratorTests()
    {
        var validator = new InlineValidator<TestCommand>();
        validator.RuleFor(command => command.Value).NotEmpty().WithErrorCode(ErrorCode);

        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _decorator = new ValidationDecorator<TestCommand, Unit>(_inner, [validator]);
    }

    [Fact]
    public async Task Handle_InvalidCommand_ReturnsValidationErrorWithoutCallingHandler()
    {
        var result = await _decorator.HandleAsync(new TestCommand(string.Empty), CancellationToken.None);

        result.Error.Code.ShouldBe(ErrorCode);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        await _inner.DidNotReceive().HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_CallsHandler()
    {
        var result = await _decorator.HandleAsync(new TestCommand("valor"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _inner.Received(1).HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    public sealed record TestCommand(string Value) : ICommand<Unit>;
}