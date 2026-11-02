using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Doctors;

public sealed class DoctorDisassociateSpecialtyTests
{
    public static IEnumerable<object[]> Removals()
    {
        yield return [11, new[] { 22, 33 }];
        yield return [22, new[] { 11, 33 }];
        yield return [33, new[] { 11, 22 }];
    }

    [Theory]
    [MemberData(nameof(Removals))]
    public void Disassociate_retira_solo_la_solicitada_y_conserva_los_demas_vinculos(int id, int[] expected)
    {
        var doctor = DoctorMother.Create([11, 22, 33]).Value;
        var before = Profile(doctor);
        var retained = doctor.Specialties.Where(link => expected.Contains(link.SpecialtyId)).ToArray();
        var view = doctor.Specialties;

        var result = doctor.DisassociateSpecialty(id);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(expected, ignoreOrder: true);
        doctor.Specialties.ShouldBe(retained);
        view.Select(link => link.SpecialtyId).ShouldBe(expected, ignoreOrder: true);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Disassociate_con_dos_asociaciones_conserva_el_perfil_y_la_activacion(bool active)
    {
        var doctor = DoctorMother.Create([11, 22]).Value;
        if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var before = Profile(doctor);
        var remaining = doctor.Specialties.Single(link => link.SpecialtyId == 22);

        var result = doctor.DisassociateSpecialty(11);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        doctor.Specialties.ShouldHaveSingleItem().ShouldBeSameAs(remaining);
        remaining.SpecialtyId.ShouldBe(22);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Disassociate_rechaza_una_no_asociada_antes_de_comprobar_la_ultima(bool onlyOne)
    {
        var ids = onlyOne ? new[] { 11 } : new[] { 11, 22 };
        var doctor = DoctorMother.Create(ids).Value;
        var before = Profile(doctor);
        var links = doctor.Specialties.ToArray();

        var result = doctor.DisassociateSpecialty(99);

        AssertConflict(result, DoctorErrors.SpecialtyNotAssociated, "Doctor.SpecialtyNotAssociated");
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(ids, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Disassociate_rechaza_la_ultima_sin_modificar_asociaciones_o_perfil(bool active)
    {
        var doctor = DoctorMother.Create([11]).Value;
        if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var before = Profile(doctor);
        var original = doctor.Specialties.Single();

        var result = doctor.DisassociateSpecialty(11);

        AssertConflict(result, DoctorErrors.CannotRemoveLastSpecialty, "Doctor.CannotRemoveLastSpecialty");
        doctor.Specialties.ShouldHaveSingleItem().ShouldBeSameAs(original);
        original.SpecialtyId.ShouldBe(11);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Disassociate_permite_sustituir_la_original_y_rechaza_quitar_la_ultima_restante()
    {
        var doctor = DoctorMother.Create([11]).Value;
        var before = Profile(doctor);

        doctor.AssociateSpecialty(22).IsSuccess.ShouldBeTrue();
        doctor.DisassociateSpecialty(11).IsSuccess.ShouldBeTrue();
        var remaining = doctor.Specialties.ShouldHaveSingleItem();
        remaining.SpecialtyId.ShouldBe(22);

        AssertConflict(doctor.DisassociateSpecialty(22), DoctorErrors.CannotRemoveLastSpecialty,
            "Doctor.CannotRemoveLastSpecialty");

        doctor.Specialties.ShouldHaveSingleItem().ShouldBeSameAs(remaining);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Disassociate_rechaza_retirar_de_nuevo_una_ya_retirada_aunque_solo_quede_otra()
    {
        var doctor = DoctorMother.Create([11, 22]).Value;
        var before = Profile(doctor);

        doctor.DisassociateSpecialty(11).IsSuccess.ShouldBeTrue();
        var remaining = doctor.Specialties.ShouldHaveSingleItem();
        AssertConflict(doctor.DisassociateSpecialty(11), DoctorErrors.SpecialtyNotAssociated,
            "Doctor.SpecialtyNotAssociated");

        doctor.Specialties.ShouldHaveSingleItem().ShouldBeSameAs(remaining);
        remaining.SpecialtyId.ShouldBe(22);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    public static IEnumerable<object[]> InvalidIds()
    {
        foreach (var ids in new[] { new[] { 11 }, new[] { 11, 22 } })
        foreach (var invalidId in new[] { 0, -1, int.MinValue })
            yield return [ids, invalidId];
    }

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public void Disassociate_prioriza_ID_invalido_sobre_pertenencia_y_ultima_sin_modificar_estado(int[] ids, int invalidId)
    {
        var doctor = DoctorMother.Create(ids).Value;
        doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var before = Profile(doctor);
        var links = doctor.Specialties.ToArray();

        var result = doctor.DisassociateSpecialty(invalidId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.InvalidId);
        result.Error.Code.ShouldBe("Specialty.InvalidId");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(ids, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        Profile(doctor).ShouldBe(before);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    private static void AssertConflict(Result result, Error expected, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(expected);
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    private static object Profile(Doctor doctor) => new
    {
        doctor.Id, doctor.UserId, doctor.FirstName, doctor.LastName, doctor.LicenseNumber,
        doctor.PhoneNumber, doctor.ProvinceId, doctor.MunicipalityId, doctor.AddressLine,
        doctor.ConsultationFee, doctor.YearsOfExperience, doctor.Biography, doctor.IsActive
    };
}
