using NSubstitute;
using SGCM.Data.Application.People.Commands.DeactivateSpecialty;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class DeactivateSpecialtyCommandHandlerTests
{
    private const int SpecialtyId = 42;
    private readonly ISpecialtyRepository _repository = Substitute.For<ISpecialtyRepository>();

    [Fact]
    public async Task Handle_cambia_el_estado_conserva_los_datos_y_propaga_el_token()
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = CreateSpecialty(true);
        _repository.GetByIdAsync(SpecialtyId, cancellation.Token).Returns(specialty);

        var result = await new DeactivateSpecialtyCommandHandler(_repository)
            .HandleAsync(new(SpecialtyId), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        result.Error.ShouldBe(Error.None);
        AssertSpecialty(specialty, false);
        await _repository.Received(1).GetByIdAsync(SpecialtyId, cancellation.Token);
        _repository.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_propaga_AlreadyInactive_sin_modificar_la_entidad_ni_realizar_otras_operaciones()
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = CreateSpecialty(false);
        _repository.GetByIdAsync(SpecialtyId, cancellation.Token).Returns(specialty);

        var result = await new DeactivateSpecialtyCommandHandler(_repository)
            .HandleAsync(new(SpecialtyId), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ActivationErrors.AlreadyInactive(nameof(Specialty)));
        result.Error.Code.ShouldBe("Specialty.AlreadyInactive");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        AssertSpecialty(specialty, false);
        await _repository.Received(1).GetByIdAsync(SpecialtyId, cancellation.Token);
        _repository.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_devuelve_NotFound_sin_realizar_otras_operaciones()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await new DeactivateSpecialtyCommandHandler(_repository)
            .HandleAsync(new(SpecialtyId), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.NotFound);
        await _repository.Received(1).GetByIdAsync(SpecialtyId, cancellation.Token);
        _repository.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_propaga_la_cancelacion_del_repositorio_sin_realizar_otras_operaciones()
    {
        using var cancellation = new CancellationTokenSource();
        _repository.GetByIdAsync(SpecialtyId, cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<Specialty?>(cancellation.Token);
        });

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            new DeactivateSpecialtyCommandHandler(_repository).HandleAsync(new(SpecialtyId), cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await _repository.Received(1).GetByIdAsync(SpecialtyId, cancellation.Token);
        _repository.ReceivedCalls().ShouldHaveSingleItem();
    }

    private static Specialty CreateSpecialty(bool active)
    {
        var specialty = Specialty.Create("Cardiología", "Atención MÉDICA.\nPara la niñez.").Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(specialty, SpecialtyId);
        if (!active)
        {
            specialty.Deactivate().IsSuccess.ShouldBeTrue();
        }
        return specialty;
    }

    private static void AssertSpecialty(Specialty specialty, bool active)
    {
        specialty.Id.ShouldBe(SpecialtyId);
        specialty.Name.ShouldBe("Cardiología");
        specialty.Description.ShouldBe("Atención MÉDICA.\nPara la niñez.");
        specialty.IsActive.ShouldBe(active);
        specialty.DomainEvents.ShouldBeEmpty();
    }
}
