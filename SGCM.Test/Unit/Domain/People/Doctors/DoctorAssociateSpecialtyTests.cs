using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Doctors;

public sealed class DoctorAssociateSpecialtyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Associate_agrega_solo_la_nueva_y_conserva_identidad_perfil_y_activacion(bool active)
    {
        var doctor = DoctorMother.Create([11]).Value;
        if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var before = Profile(doctor);
        var original = doctor.Specialties.Single();

        var result = doctor.AssociateSpecialty(22);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22 }, ignoreOrder: true);
        doctor.Specialties.Single(link => link.SpecialtyId == 11).ShouldBeSameAs(original);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Associate_permite_varias_especialidades_distintas_sin_perder_las_anteriores()
    {
        var doctor = DoctorMother.Create([11]).Value;

        doctor.AssociateSpecialty(22).IsSuccess.ShouldBeTrue();
        doctor.AssociateSpecialty(33).IsSuccess.ShouldBeTrue();
        doctor.AssociateSpecialty(int.MaxValue).IsSuccess.ShouldBeTrue();

        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22, 33, int.MaxValue }, ignoreOrder: true);
    }

    public static IEnumerable<object[]> Duplicates()
    {
        yield return [new[] { 11 }, 11];
        yield return [new[] { 11, 22 }, 11];
        yield return [new[] { 11, 22 }, 22];
    }

    [Theory]
    [MemberData(nameof(Duplicates))]
    public void Associate_rechaza_duplicados_sin_modificar_ningun_estado(int[] ids, int duplicate)
    {
        var doctor = DoctorMother.Create(ids).Value;
        var before = Profile(doctor);
        var links = doctor.Specialties.ToArray();

        var result = doctor.AssociateSpecialty(duplicate);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(DoctorErrors.SpecialtyAlreadyAssociated);
        result.Error.Code.ShouldBe("Doctor.SpecialtyAlreadyAssociated");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(ids, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Specialties_impide_modificaciones_externas_incluso_mediante_interfaces_mutables()
    {
        var doctor = DoctorMother.Create([11, 22]).Value;
        var view = doctor.Specialties;
        var first = view.First();
        var collection = view.ShouldBeAssignableTo<ICollection<DoctorSpecialty>>();
        var list = view.ShouldBeAssignableTo<IList<DoctorSpecialty>>();
        var untyped = view.ShouldBeAssignableTo<System.Collections.IList>();

        collection.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => collection.Clear());
        Should.Throw<NotSupportedException>(() => collection.Remove(first));
        Should.Throw<NotSupportedException>(() => collection.Add(first));
        Should.Throw<NotSupportedException>(() => list[0] = first);
        Should.Throw<NotSupportedException>(() => untyped.RemoveAt(0));

        doctor.AssociateSpecialty(33).IsSuccess.ShouldBeTrue();

        view.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22, 33 }, ignoreOrder: true);
        Should.Throw<NotSupportedException>(() => collection.Clear());
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22, 33 }, ignoreOrder: true);
    }

    [Fact]
    public void Associate_no_comparte_asociaciones_entre_perfiles()
    {
        var first = DoctorMother.Create([11]).Value;
        var second = DoctorMother.Create([11]).Value;

        first.AssociateSpecialty(22).IsSuccess.ShouldBeTrue();

        first.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22 }, ignoreOrder: true);
        second.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(11);
        first.Specialties.First().ShouldNotBeSameAs(second.Specialties.Single());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Associate_rechaza_IDs_no_positivos_sin_mutar_asociaciones_o_perfil(int invalidId)
    {
        var doctor = DoctorMother.Create([11, 22]).Value;
        doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var before = Profile(doctor);
        var links = doctor.Specialties.ToArray();

        var result = doctor.AssociateSpecialty(invalidId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.InvalidId);
        result.Error.Code.ShouldBe("Specialty.InvalidId");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22 }, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    private static object Profile(Doctor doctor) => new
    {
        doctor.Id, doctor.UserId, doctor.FirstName, doctor.LastName, doctor.LicenseNumber,
        doctor.PhoneNumber, doctor.ProvinceId, doctor.MunicipalityId, doctor.AddressLine,
        doctor.ConsultationFee, doctor.YearsOfExperience, doctor.Biography, doctor.IsActive
    };
}
