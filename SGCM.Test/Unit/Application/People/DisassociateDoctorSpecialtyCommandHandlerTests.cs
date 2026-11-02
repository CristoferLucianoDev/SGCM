using NSubstitute;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class DisassociateDoctorSpecialtyCommandHandlerTests
{
    private readonly ICurrentUser _current = Substitute.For<ICurrentUser>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();

    public DisassociateDoctorSpecialtyCommandHandlerTests() => _current.UserId.Returns(DoctorCommandData.UserId);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_retira_solo_la_solicitada_del_perfil_actual_y_conserva_los_datos(bool active)
    {
        using var cancellation = new CancellationTokenSource();
        var doctor = DoctorCommandData.Doctor(11, 22, 33);
        if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var profile = DoctorCommandData.Profile(doctor);
        var remaining = doctor.Specialties.Where(link => link.SpecialtyId != 22).ToArray();
        _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token).Returns(doctor);

        var result = await Handler().HandleAsync(new(22), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        result.Error.ShouldBe(Error.None);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 33 }, ignoreOrder: true);
        doctor.Specialties.ShouldBe(remaining);
        DoctorCommandData.Profile(doctor).ShouldBe(profile);
        doctor.DomainEvents.ShouldBeEmpty();
        await AssertCalls(cancellation.Token);
    }

    [Theory]
    [InlineData(false, "Doctor.NotFound", ErrorType.NotFound)]
    [InlineData(true, "Doctor.ProfileIncomplete", ErrorType.Conflict)]
    public async Task Handle_rechaza_el_perfil_ausente_o_legado_sin_realizar_otras_operaciones(
        bool legacy, string code, ErrorType type)
    {
        var doctor = legacy ? DoctorCommandData.Legacy() : null;
        if (doctor is not null)
            _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, CancellationToken.None).Returns(doctor);
        var profile = doctor is null ? null : DoctorCommandData.Profile(doctor);

        var result = await Handler().HandleAsync(new(11), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
        result.Error.ShouldBe(legacy ? DoctorErrors.ProfileIncomplete : DoctorErrors.NotFound);
        if (doctor is not null)
        {
            doctor.Specialties.ShouldBeEmpty();
            DoctorCommandData.Profile(doctor).ShouldBe(profile);
        }
        await AssertCalls(CancellationToken.None);
    }

    public static IEnumerable<object[]> Conflicts()
    {
        yield return [new[] { 11 }, 11, "Doctor.CannotRemoveLastSpecialty"];
        yield return [new[] { 11 }, 99, "Doctor.SpecialtyNotAssociated"];
        yield return [new[] { 11, 22 }, 99, "Doctor.SpecialtyNotAssociated"];
    }

    [Theory]
    [MemberData(nameof(Conflicts))]
    public async Task Handle_propaga_conflictos_con_la_prioridad_del_dominio_y_sin_mutacion(
        int[] ids, int target, string code)
    {
        var doctor = DoctorCommandData.Doctor(ids);
        var profile = DoctorCommandData.Profile(doctor);
        var links = doctor.Specialties.ToArray();
        _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, CancellationToken.None).Returns(doctor);

        var result = await Handler().HandleAsync(new(target), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(ids, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        DoctorCommandData.Profile(doctor).ShouldBe(profile);
        doctor.DomainEvents.ShouldBeEmpty();
        await AssertCalls(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_propaga_cancelacion_del_repositorio()
    {
        using var cancellation = new CancellationTokenSource();
        _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<Doctor?>(cancellation.Token);
        });

        var error = await Should.ThrowAsync<OperationCanceledException>(() => Handler().HandleAsync(new(11), cancellation.Token));

        error.CancellationToken.ShouldBe(cancellation.Token);
        await AssertCalls(cancellation.Token);
    }

    private DisassociateDoctorSpecialtyCommandHandler Handler() => new(_current, _doctors);

    private async Task AssertCalls(CancellationToken token)
    {
        _ = _current.Received(1).UserId;
        await _doctors.Received(1).GetByUserIdForUpdateAsync(DoctorCommandData.UserId, token);
        _doctors.ReceivedCalls().ShouldHaveSingleItem();
    }
}
