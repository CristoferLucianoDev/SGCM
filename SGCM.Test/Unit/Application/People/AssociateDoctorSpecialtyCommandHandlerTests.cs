using System.Reflection;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class AssociateDoctorSpecialtyCommandHandlerTests
{
    private readonly ICurrentUser _current = Substitute.For<ICurrentUser>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly ISpecialtyRepository _specialties = Substitute.For<ISpecialtyRepository>();

    public AssociateDoctorSpecialtyCommandHandlerTests() => _current.UserId.Returns(DoctorCommandData.UserId);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_asocia_solo_en_el_perfil_del_usuario_actual_y_conserva_el_resto(bool active)
    {
        using var cancellation = new CancellationTokenSource();
        var doctor = DoctorCommandData.Doctor(11);
        if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var profile = DoctorCommandData.Profile(doctor);
        var original = doctor.Specialties.Single();
        var specialty = DoctorCommandData.Specialty(22);
        var catalog = DoctorCommandData.SpecialtyState(specialty);
        _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token).Returns(doctor);
        _specialties.GetByIdAsync(22, cancellation.Token).Returns(specialty);

        var result = await Handler().HandleAsync(new(22), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        result.Error.ShouldBe(Error.None);
        doctor.Id.ShouldBe(DoctorCommandData.DoctorId);
        doctor.UserId.ShouldBe(DoctorCommandData.UserId);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22 }, ignoreOrder: true);
        doctor.Specialties.Single(link => link.SpecialtyId == 11).ShouldBeSameAs(original);
        DoctorCommandData.Profile(doctor).ShouldBe(profile);
        specialty.IsActive.ShouldBeTrue();
        DoctorCommandData.SpecialtyState(specialty).ShouldBe(catalog);
        doctor.DomainEvents.ShouldBeEmpty();
        await AssertCalls(cancellation.Token, 22);
    }

    [Theory]
    [InlineData(false, "Doctor.NotFound", ErrorType.NotFound)]
    [InlineData(true, "Doctor.ProfileIncomplete", ErrorType.Conflict)]
    public async Task Handle_rechaza_perfiles_ausentes_o_legados_antes_de_consultar_catalogo(
        bool legacy, string code, ErrorType type)
    {
        using var cancellation = new CancellationTokenSource();
        var doctor = legacy ? DoctorCommandData.Legacy() : null;
        if (doctor is not null)
            _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token).Returns(doctor);
        var profile = doctor is null ? null : DoctorCommandData.Profile(doctor);

        var result = await Handler().HandleAsync(new(22), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
        result.Error.ShouldBe(legacy ? DoctorErrors.ProfileIncomplete : DoctorErrors.NotFound);
        _specialties.ReceivedCalls().ShouldBeEmpty();
        await _doctors.Received(1).GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token);
        _doctors.ReceivedCalls().ShouldHaveSingleItem();
        if (doctor is not null)
        {
            doctor.Specialties.ShouldBeEmpty();
            DoctorCommandData.Profile(doctor).ShouldBe(profile);
        }
    }

    [Theory]
    [InlineData("missing", "Specialty.NotFound", ErrorType.NotFound)]
    [InlineData("inactive", "Specialty.Inactive", ErrorType.Conflict)]
    [InlineData("duplicate", "Doctor.SpecialtyAlreadyAssociated", ErrorType.Conflict)]
    [InlineData("inactiveDuplicate", "Specialty.Inactive", ErrorType.Conflict)]
    public async Task Handle_propaga_errores_sin_mutar_y_verifica_actividad_antes_de_asociar(
        string scenario, string code, ErrorType type)
    {
        using var cancellation = new CancellationTokenSource();
        var doctor = DoctorCommandData.Doctor(11, 33);
        var profile = DoctorCommandData.Profile(doctor);
        var links = doctor.Specialties.ToArray();
        var id = scenario is "duplicate" or "inactiveDuplicate" ? 11 : 22;
        _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token).Returns(doctor);
        Specialty? specialty = scenario == "missing" ? null
            : DoctorCommandData.Specialty(id, scenario == "duplicate");
        _specialties.GetByIdAsync(id, cancellation.Token).Returns(specialty);
        var catalog = specialty is null ? null : DoctorCommandData.SpecialtyState(specialty);

        var result = await Handler().HandleAsync(new(id), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 33 }, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        DoctorCommandData.Profile(doctor).ShouldBe(profile);
        doctor.DomainEvents.ShouldBeEmpty();
        if (specialty is not null)
        {
            specialty.IsActive.ShouldBe(scenario == "duplicate");
            DoctorCommandData.SpecialtyState(specialty).ShouldBe(catalog);
        }
        await AssertCalls(cancellation.Token, id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_propaga_cancelacion_de_cada_repositorio_sin_modificar_el_agregado(bool duringDoctor)
    {
        using var cancellation = new CancellationTokenSource();
        var doctor = DoctorCommandData.Doctor(11);
        var profile = DoctorCommandData.Profile(doctor);
        if (duringDoctor)
            _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token).Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<Doctor?>(cancellation.Token);
            });
        else
        {
            _doctors.GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token).Returns(doctor);
            _specialties.GetByIdAsync(22, cancellation.Token).Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<Specialty?>(cancellation.Token);
            });
        }

        var error = await Should.ThrowAsync<OperationCanceledException>(() => Handler().HandleAsync(new(22), cancellation.Token));

        error.CancellationToken.ShouldBe(cancellation.Token);
        doctor.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(11);
        DoctorCommandData.Profile(doctor).ShouldBe(profile);
        await _doctors.Received(1).GetByUserIdForUpdateAsync(DoctorCommandData.UserId, cancellation.Token);
        if (duringDoctor) _specialties.ReceivedCalls().ShouldBeEmpty();
        else await _specialties.Received(1).GetByIdAsync(22, cancellation.Token);
    }

    private AssociateDoctorSpecialtyCommandHandler Handler() => new(_current, _doctors, _specialties);

    private async Task AssertCalls(CancellationToken token, int specialtyId)
    {
        _ = _current.Received(1).UserId;
        await _doctors.Received(1).GetByUserIdForUpdateAsync(DoctorCommandData.UserId, token);
        await _specialties.Received(1).GetByIdAsync(specialtyId, token);
        _doctors.ReceivedCalls().ShouldHaveSingleItem();
        _specialties.ReceivedCalls().ShouldHaveSingleItem();
    }
}

internal static class DoctorCommandData
{
    public const int UserId = 42;
    public const int DoctorId = 77;

    public static Doctor Doctor(params int[] ids)
    {
        var doctor = DoctorMother.Create(ids, userId: UserId).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(doctor, DoctorId);
        return doctor;
    }

    public static Doctor Legacy()
    {
        var doctor = Doctor(11);
        // Emula un dato legado materializado por EF; nunca abre una fábrica pública vacía.
        var links = (List<DoctorSpecialty>)typeof(Doctor).GetField("_specialties", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(doctor)!;
        links.Clear();
        return doctor;
    }

    public static Specialty Specialty(int id, bool active = true)
    {
        var specialty = SGCM.Data.Domain.People.Specialties.Specialty.Create("Cardiología", "Descripción.").Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(specialty, id);
        if (!active) specialty.Deactivate().IsSuccess.ShouldBeTrue();
        return specialty;
    }

    public static object SpecialtyState(Specialty specialty) => new
    {
        specialty.Id, specialty.Name, specialty.Description, specialty.IsActive
    };

    public static object Profile(Doctor doctor) => new
    {
        doctor.Id, doctor.UserId, doctor.FirstName, doctor.LastName, doctor.LicenseNumber, doctor.PhoneNumber,
        doctor.ProvinceId, doctor.MunicipalityId, doctor.AddressLine, doctor.ConsultationFee,
        doctor.YearsOfExperience, doctor.Biography, doctor.IsActive
    };
}
