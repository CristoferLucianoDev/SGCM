using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public class CreateSpecialtyCommandHandlerTests
{
    private readonly ISpecialtyRepository _repository = Substitute.For<ISpecialtyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_normaliza_y_guarda_el_agregado_activo_respetando_el_token()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new CreateSpecialtyCommandHandler(_repository, _unitOfWork);
        var command = new CreateSpecialtyCommand("  ATENCIÓN A LA NIÑEZ  ", "  Atención  MÉDICA.  ");

        var result = await handler.HandleAsync(command, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _repository.ExistsByNameAsync("Atención A La Niñez", null, cancellation.Token);
            _repository.Add(Arg.Is<Specialty>(value => value.Name == "Atención A La Niñez"
                && value.Description == "Atención  MÉDICA." && value.IsActive));
            _unitOfWork.SaveChangesAsync(cancellation.Token);
        });
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    [Theory]
    [InlineData(null, "Descripción válida.", "Specialty.NameRequired")]
    [InlineData("", "Descripción válida.", "Specialty.NameRequired")]
    [InlineData("Cardiología", null, "Specialty.DescriptionRequired")]
    [InlineData("Cardiología", "   ", "Specialty.DescriptionRequired")]
    public async Task Handle_rechaza_datos_invalidos_sin_consultar_ni_guardar(
        string? name, string? description, string expectedCode)
    {
        var handler = new CreateSpecialtyCommandHandler(_repository, _unitOfWork);

        var result = await handler.HandleAsync(new CreateSpecialtyCommand(name!, description!), CancellationToken.None);

        result.Error.Code.ShouldBe(expectedCode);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<Specialty>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_nombre_duplicado_no_agrega_ni_guarda()
    {
        _repository.ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateSpecialtyCommandHandler(_repository, _unitOfWork);

        var result = await handler.HandleAsync(new CreateSpecialtyCommand("Cardiología", "Descripción válida."), CancellationToken.None);

        result.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
        _repository.DidNotReceive().Add(Arg.Any<Specialty>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_conflicto_al_guardar_lo_propaga_al_decorador_sin_reintentar()
    {
        var conflict = new PersistenceConflictException(SpecialtyErrors.NameAlreadyInUse, new InvalidOperationException());
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(conflict));
        var handler = new CreateSpecialtyCommandHandler(_repository, _unitOfWork);

        var actual = await Should.ThrowAsync<PersistenceConflictException>(() =>
            handler.HandleAsync(new CreateSpecialtyCommand("Cardiología", "Descripción válida."), CancellationToken.None));

        actual.ShouldBeSameAs(conflict);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_cancelacion_o_error_inesperado_se_propaga_sin_confirmar(bool duringSave, bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        Exception original = canceled
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("Fallo de prueba.");
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        var handler = new TransactionDecorator<CreateSpecialtyCommand, int>(
            new CreateSpecialtyCommandHandler(_repository, _unitOfWork), _unitOfWork);

        if (duringSave)
        {
            _unitOfWork.SaveChangesAsync(cancellation.Token).Returns(_ =>
            {
                if (canceled)
                {
                    cancellation.Cancel();
                }

                return Task.FromException(original);
            });
        }
        else
        {
            _repository.ExistsByNameAsync("Cardiología", null, cancellation.Token).Returns(_ =>
            {
                if (canceled)
                {
                    cancellation.Cancel();
                }

                return Task.FromException<bool>(original);
            });
        }

        var actual = await Record.ExceptionAsync(() => handler.HandleAsync(
            new CreateSpecialtyCommand("Cardiología", "Descripción válida."), cancellation.Token));

        actual.ShouldBeSameAs(original);
        await _repository.Received(1).ExistsByNameAsync("Cardiología", null, cancellation.Token);
        _repository.Received(duringSave ? 1 : 0).Add(Arg.Any<Specialty>());
        await _unitOfWork.Received(duringSave ? 1 : 0).SaveChangesAsync(cancellation.Token);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
    }
}
