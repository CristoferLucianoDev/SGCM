using System.Reflection;
using NSubstitute;
using SGCM.Data.Application.People.Commands.UpdateSpecialty;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.People.Specialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class UpdateSpecialtyCommandHandlerTests
{
    private const int SpecialtyId = 42;
    private const string OriginalName = "Cardiología";
    private const string OriginalDescription = "Atención  MÉDICA.\nPara la niñez.";
    private readonly ISpecialtyRepository _repository = Substitute.For<ISpecialtyRepository>();
    private readonly IDomainEvent _pendingEvent = Substitute.For<IDomainEvent>();

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task Handle_actualiza_solo_los_campos_seleccionados_conservando_identidad_estado_y_eventos(
        bool nameProvided, bool descriptionProvided, bool active)
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = CreateSpecialty(active);
        _repository.GetByIdAsync(SpecialtyId, cancellation.Token).Returns(specialty);
        _repository.ExistsByNameAsync("Atención A La Niñez", SpecialtyId, cancellation.Token).Returns(_ =>
        {
            AssertOriginal(specialty, active);
            return false;
        });
        var command = new UpdateSpecialtyCommand(SpecialtyId, nameProvided, " \tATENCIÓN A LA NIÑEZ  ",
            descriptionProvided, "  Nueva  descripción.\nCon ñ.  ");

        var result = await new UpdateSpecialtyCommandHandler(_repository).HandleAsync(command, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        result.Error.ShouldBe(Error.None);
        specialty.Name.ShouldBe(nameProvided ? "Atención A La Niñez" : OriginalName);
        specialty.Description.ShouldBe(descriptionProvided ? "Nueva  descripción.\nCon ñ." : OriginalDescription);
        AssertIdentityAndState(specialty, active);
        await _repository.Received(1).GetByIdAsync(SpecialtyId, cancellation.Token);
        await _repository.Received(nameProvided ? 1 : 0)
            .ExistsByNameAsync("Atención A La Niñez", SpecialtyId, cancellation.Token);
        _repository.DidNotReceive().Add(Arg.Any<Specialty>());
    }

    public static IEnumerable<object?[]> InvalidUpdates()
    {
        foreach (var value in new string?[] { null, "", " \t\r\n " })
        {
            yield return [true, value, true, "Descripción nueva.", "Specialty.NameRequired"];
            yield return [true, "Neurología", true, value, "Specialty.DescriptionRequired"];
        }
        yield return [true, new string('a', 101), true, "Descripción nueva.", "Specialty.NameTooLong"];
        yield return [true, "Neurología", true, new string('b', 501), "Specialty.DescriptionTooLong"];
        yield return [false, null, false, null, "Specialty.NoFieldsProvided"];
        yield return [false, "Ignorado", false, "Ignorada", "Specialty.NoFieldsProvided"];
        yield return [true, null, true, null, "Specialty.NameRequired"];
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Handle_rechaza_la_actualizacion_invalida_sin_mutar_ni_consultar_unicidad(
        bool nameProvided, string? name, bool descriptionProvided, string? description, string expectedCode)
    {
        var specialty = CreateSpecialty(false);
        _repository.GetByIdAsync(SpecialtyId, CancellationToken.None).Returns(specialty);

        var result = await new UpdateSpecialtyCommandHandler(_repository).HandleAsync(
            new(SpecialtyId, nameProvided, name, descriptionProvided, description), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
        AssertOriginal(specialty, false);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<Specialty>());
    }

    [Fact]
    public async Task Handle_especialidad_inexistente_devuelve_NotFound()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await new UpdateSpecialtyCommandHandler(_repository).HandleAsync(
            new(SpecialtyId, true, "Neurología", false, null), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.NotFound);
        result.Error.Code.ShouldBe("Specialty.NotFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        await _repository.Received(1).GetByIdAsync(SpecialtyId, cancellation.Token);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<Specialty>());
    }

    [Theory]
    [InlineData("Cardiología", "Cardiología")]
    [InlineData("  CARDIOLOGÍA  ", "Cardiología")]
    [InlineData("cardiologia", "Cardiologia")]
    public async Task Handle_permite_el_mismo_nombre_o_una_variante_y_excluye_el_id_propio(string name, string expected)
    {
        var specialty = CreateSpecialty();
        _repository.GetByIdAsync(SpecialtyId, CancellationToken.None).Returns(specialty);

        var result = await new UpdateSpecialtyCommandHandler(_repository).HandleAsync(
            new(SpecialtyId, true, name, false, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        specialty.Name.ShouldBe(expected);
        specialty.Description.ShouldBe(OriginalDescription);
        AssertIdentityAndState(specialty, true);
        await _repository.Received(1).ExistsByNameAsync(expected, SpecialtyId, CancellationToken.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_nombre_de_otra_especialidad_no_muta_el_agregado(bool active)
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = CreateSpecialty(active);
        _repository.GetByIdAsync(SpecialtyId, cancellation.Token).Returns(specialty);
        _repository.ExistsByNameAsync("Neurología", SpecialtyId, cancellation.Token).Returns(true);

        var result = await new UpdateSpecialtyCommandHandler(_repository).HandleAsync(
            new(SpecialtyId, true, "  NEUROLOGÍA ", true, "Descripción nueva."), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
        AssertOriginal(specialty, active);
        await _repository.Received(1).ExistsByNameAsync("Neurología", SpecialtyId, cancellation.Token);
        _repository.DidNotReceive().Add(Arg.Any<Specialty>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_conserva_exactamente_el_campo_omitido_e_ignora_su_valor_invalido(bool nameProvided)
    {
        var specialty = CreateSpecialty();
        var original = nameProvided ? " \tDescripción  ORIGINAL. " : " \tCARDIOLOGÍA  PEDIÁTRICA ";
        typeof(Specialty).GetProperty(nameProvided ? nameof(Specialty.Description) : nameof(Specialty.Name))!
            .SetValue(specialty, original);
        _repository.GetByIdAsync(SpecialtyId, CancellationToken.None).Returns(specialty);

        var result = await new UpdateSpecialtyCommandHandler(_repository).HandleAsync(
            new(SpecialtyId, nameProvided, nameProvided ? "Neurología" : new string('a', 101),
                !nameProvided, nameProvided ? new string('b', 501) : "Descripción nueva."), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        specialty.Name.ShouldBe(nameProvided ? "Neurología" : original);
        specialty.Description.ShouldBe(nameProvided ? original : "Descripción nueva.");
        AssertIdentityAndState(specialty, true);
        if (!nameProvided)
        {
            await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_propaga_cancelacion_y_errores_sin_mutar(bool duringUniqueness, bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = CreateSpecialty();
        Exception original = canceled
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("Fallo de prueba.");
        if (canceled)
        {
            cancellation.Cancel();
        }
        _repository.GetByIdAsync(SpecialtyId, cancellation.Token)
            .Returns(_ => duringUniqueness ? Task.FromResult<Specialty?>(specialty) : Task.FromException<Specialty?>(original));
        _repository.ExistsByNameAsync("Neurología", SpecialtyId, cancellation.Token)
            .Returns(Task.FromException<bool>(original));

        var actual = await Record.ExceptionAsync(() => new UpdateSpecialtyCommandHandler(_repository).HandleAsync(
            new(SpecialtyId, true, "Neurología", false, null), cancellation.Token));

        actual.ShouldBeSameAs(original);
        AssertOriginal(specialty, true);
        await _repository.Received(1).GetByIdAsync(SpecialtyId, cancellation.Token);
        await _repository.Received(duringUniqueness ? 1 : 0)
            .ExistsByNameAsync("Neurología", SpecialtyId, cancellation.Token);
        _repository.DidNotReceive().Add(Arg.Any<Specialty>());
    }

    private Specialty CreateSpecialty(bool active = true)
    {
        var specialty = Specialty.Create(OriginalName, OriginalDescription).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(specialty, SpecialtyId);
        if (!active)
        {
            specialty.Deactivate().IsSuccess.ShouldBeTrue();
        }
        typeof(AggregateRoot).GetMethod("Raise", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(specialty, [_pendingEvent]);
        return specialty;
    }

    private void AssertOriginal(Specialty specialty, bool active)
    {
        specialty.Name.ShouldBe(OriginalName);
        specialty.Description.ShouldBe(OriginalDescription);
        AssertIdentityAndState(specialty, active);
    }

    private void AssertIdentityAndState(Specialty specialty, bool active)
    {
        specialty.Id.ShouldBe(SpecialtyId);
        specialty.IsActive.ShouldBe(active);
        specialty.DomainEvents.Count.ShouldBe(1);
        specialty.DomainEvents.Single().ShouldBeSameAs(_pendingEvent);
    }
}
