using System.Reflection;
using NSubstitute;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Doctors;

public sealed class DoctorUpdateProfileTests
{
    private static readonly Profile NewProfile = new(
        "Ana", "Gómez", "LIC-250", "(809) 555-0199", 5, 8,
        "Calle  Principal", 1250.34m, 20, "Medicina  preventiva.");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateProfile_actualiza_y_normaliza_el_perfil_conservando_identidad_y_especialidades(bool active)
    {
        var doctor = DoctorMother.Create([11, 22]).Value;
        if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var specialties = doctor.Specialties.ToArray();
        var input = NewProfile with
        {
            FirstName = " Ana ", LastName = " Gómez\t", LicenseNumber = " LIC-250 ",
            PhoneNumber = " (809) 555-0199 ", AddressLine = " Calle  Principal ",
            ConsultationFee = 1250.340m, Biography = " Medicina  preventiva. "
        };

        input.ApplyTo(doctor).IsSuccess.ShouldBeTrue();

        Profile.Read(doctor).ShouldBe(NewProfile);
        doctor.Id.ShouldBe(0);
        doctor.UserId.ShouldBe(DoctorMother.UserId);
        doctor.Specialties.ShouldBe(specialties);
        doctor.IsActive.ShouldBe(active);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("FirstNameRequired")]
    [InlineData("FirstNameTooLong")]
    [InlineData("LastNameRequired")]
    [InlineData("LastNameTooLong")]
    [InlineData("LicenseNumberRequired")]
    [InlineData("LicenseNumberTooLong")]
    [InlineData("PhoneNumberRequired")]
    [InlineData("PhoneNumberTooLong")]
    [InlineData("InvalidProvinceId")]
    [InlineData("InvalidMunicipalityId")]
    [InlineData("AddressLineRequired")]
    [InlineData("AddressLineTooLong")]
    [InlineData("InvalidConsultationFee")]
    [InlineData("InvalidYearsOfExperience")]
    [InlineData("BiographyRequired")]
    [InlineData("BiographyTooLong")]
    public void UpdateProfile_devuelve_el_mismo_error_que_Create_y_conserva_el_estado(string error)
    {
        var input = error switch
        {
            "FirstNameRequired" => NewProfile with { FirstName = null },
            "FirstNameTooLong" => NewProfile with { FirstName = new string('a', 101) },
            "LastNameRequired" => NewProfile with { LastName = " " },
            "LastNameTooLong" => NewProfile with { LastName = new string('a', 101) },
            "LicenseNumberRequired" => NewProfile with { LicenseNumber = "" },
            "LicenseNumberTooLong" => NewProfile with { LicenseNumber = new string('a', 51) },
            "PhoneNumberRequired" => NewProfile with { PhoneNumber = "\t " },
            "PhoneNumberTooLong" => NewProfile with { PhoneNumber = new string('1', 21) },
            "InvalidProvinceId" => NewProfile with { ProvinceId = 0 },
            "InvalidMunicipalityId" => NewProfile with { MunicipalityId = -1 },
            "AddressLineRequired" => NewProfile with { AddressLine = null },
            "AddressLineTooLong" => NewProfile with { AddressLine = new string('a', 251) },
            "InvalidConsultationFee" => NewProfile with { ConsultationFee = 12.345m },
            "InvalidYearsOfExperience" => NewProfile with { YearsOfExperience = int.MinValue },
            "BiographyRequired" => NewProfile with { Biography = null },
            "BiographyTooLong" => NewProfile with { Biography = new string('a', 1001) },
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
        var doctor = DoctorMother.Create([11, 22]).Value;
        doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var before = Profile.Read(doctor);
        var specialties = doctor.Specialties.ToArray();

        var update = input.ApplyTo(doctor);
        var create = input.CreateDoctor();

        update.IsFailure.ShouldBeTrue();
        update.Error.Code.ShouldBe($"Doctor.{error}");
        update.Error.Type.ShouldBe(ErrorType.Validation);
        create.IsFailure.ShouldBeTrue();
        create.Error.ShouldBe(update.Error);
        Profile.Read(doctor).ShouldBe(before);
        doctor.Id.ShouldBe(0);
        doctor.UserId.ShouldBe(DoctorMother.UserId);
        doctor.Specialties.ShouldBe(specialties);
        doctor.IsActive.ShouldBeFalse();
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateProfile_conserva_un_evento_pendiente_tanto_si_tiene_exito_como_si_falla(bool valid)
    {
        var doctor = DoctorMother.Create([11]).Value;
        var pendingEvent = Substitute.For<IDomainEvent>();
        typeof(AggregateRoot).GetMethod("Raise", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(doctor, [pendingEvent]);
        var input = NewProfile with { Biography = valid ? NewProfile.Biography : null };

        input.ApplyTo(doctor).IsSuccess.ShouldBe(valid);

        doctor.DomainEvents.ShouldHaveSingleItem().ShouldBeSameAs(pendingEvent);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void UpdateProfessionalData_preserva_identidad_legada_estado_especialidades_y_eventos(bool active, bool valid)
    {
        var doctor = DoctorMother.Create([11, 22]).Value;
        if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        typeof(Entity).GetProperty(nameof(Doctor.Id))!.SetValue(doctor, 17);
        typeof(Doctor).GetProperty(nameof(Doctor.FirstName))!.SetValue(doctor, " Luis ");
        typeof(Doctor).GetProperty(nameof(Doctor.LastName))!.SetValue(doctor, " Pérez ");
        typeof(Doctor).GetProperty(nameof(Doctor.LicenseNumber))!.SetValue(doctor, " LIC-LEGADA ");
        var before = Profile.Read(doctor);
        var specialties = doctor.Specialties.ToArray();
        var pendingEvent = Substitute.For<IDomainEvent>();
        typeof(AggregateRoot).GetMethod("Raise", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(doctor, [pendingEvent]);

        var result = doctor.UpdateProfessionalData(" (809) 555-0199 ", 5, 8, " Calle  Principal ",
            1250.340m, 20, valid ? " Medicina  preventiva. " : null);

        result.IsSuccess.ShouldBe(valid);
        if (!valid) result.Error.ShouldBe(DoctorErrors.BiographyRequired);
        var expected = valid ? NewProfile with
        {
            FirstName = before.FirstName, LastName = before.LastName, LicenseNumber = before.LicenseNumber
        } : before;
        Profile.Read(doctor).ShouldBe(expected);
        doctor.Id.ShouldBe(17);
        doctor.UserId.ShouldBe(DoctorMother.UserId);
        doctor.IsActive.ShouldBe(active);
        doctor.Specialties.ShouldBe(specialties);
        doctor.DomainEvents.ShouldHaveSingleItem().ShouldBeSameAs(pendingEvent);
    }

    private sealed record Profile(
        string? FirstName, string? LastName, string? LicenseNumber, string? PhoneNumber,
        int ProvinceId, int MunicipalityId, string? AddressLine, decimal ConsultationFee,
        int YearsOfExperience, string? Biography)
    {
        public Result ApplyTo(Doctor doctor) => doctor.UpdateProfile(
            FirstName, LastName, LicenseNumber, PhoneNumber, ProvinceId, MunicipalityId,
            AddressLine, ConsultationFee, YearsOfExperience, Biography);

        public Result<Doctor> CreateDoctor() => Doctor.Create(
            DoctorMother.UserId, FirstName, LastName, LicenseNumber, PhoneNumber, ProvinceId, MunicipalityId,
            AddressLine, ConsultationFee, YearsOfExperience, Biography, [11]);

        public static Profile Read(Doctor doctor) => new(
            doctor.FirstName, doctor.LastName, doctor.LicenseNumber, doctor.PhoneNumber.Value,
            doctor.ProvinceId, doctor.MunicipalityId, doctor.AddressLine, doctor.ConsultationFee.Value,
            doctor.YearsOfExperience, doctor.Biography);
    }
}
