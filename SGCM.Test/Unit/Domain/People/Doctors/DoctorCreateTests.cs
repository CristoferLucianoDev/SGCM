using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.People.Doctors;

public sealed class DoctorCreateTests
{
    public static IEnumerable<object[]> ValidSpecialties()
    {
        yield return [new[] { 11 }];
        yield return [new[] { 11, 22, 33 }];
    }

    [Theory]
    [MemberData(nameof(ValidSpecialties))]
    public void Create_con_especialidades_validas_conserva_el_perfil_y_crea_una_identidad_nueva(int[] ids)
    {
        var result = DoctorMother.Create(ids);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
        var doctor = result.Value;
        doctor.ShouldBeAssignableTo<ActivatableAggregateRoot>();
        doctor.Id.ShouldBe(0);
        doctor.UserId.ShouldBe(DoctorMother.UserId);
        doctor.FirstName.ShouldBe(DoctorMother.FirstName);
        doctor.LastName.ShouldBe(DoctorMother.LastName);
        doctor.LicenseNumber.ShouldBe(DoctorMother.LicenseNumber);
        doctor.PhoneNumber.ShouldBe(DoctorMother.PhoneNumber);
        doctor.ProvinceId.ShouldBe(DoctorMother.ProvinceId);
        doctor.MunicipalityId.ShouldBe(DoctorMother.MunicipalityId);
        doctor.AddressLine.ShouldBe(DoctorMother.AddressLine);
        doctor.ConsultationFee.ShouldBe(DoctorMother.ConsultationFee);
        doctor.YearsOfExperience.ShouldBe(DoctorMother.YearsOfExperience);
        doctor.Biography.ShouldBe(DoctorMother.Biography);
        doctor.IsActive.ShouldBeTrue();
        doctor.Specialties.Select(value => value.SpecialtyId).ShouldBe(ids, ignoreOrder: true);
        doctor.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_rechaza_una_coleccion_nula() =>
        AssertFailure(DoctorMother.Create(null), DoctorErrors.SpecialtyRequired, "Doctor.SpecialtyRequired");

    [Fact]
    public void Create_rechaza_una_coleccion_vacia() =>
        AssertFailure(DoctorMother.Create([]), DoctorErrors.SpecialtyRequired, "Doctor.SpecialtyRequired");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_rechaza_IDs_de_especialidad_invalidos_sin_modificar_la_entrada(int invalidId)
    {
        var ids = new[] { 11, invalidId, 22 };

        AssertFailure(DoctorMother.Create(ids), SpecialtyErrors.InvalidId, "Specialty.InvalidId");

        ids.ShouldBe(new[] { 11, invalidId, 22 });
    }

    [Theory]
    [InlineData(11, 11)]
    [InlineData(11, 22, 11)]
    public void Create_rechaza_duplicados_sin_deduplicar_ni_modificar_la_entrada(params int[] ids)
    {
        var before = ids.ToArray();

        AssertFailure(DoctorMother.Create(ids), DoctorErrors.SpecialtyAlreadyAssociated,
            "Doctor.SpecialtyAlreadyAssociated", ErrorType.Conflict);

        ids.ShouldBe(before);
    }

    [Fact]
    public void Create_copia_defensivamente_la_coleccion_y_sus_elementos()
    {
        var ids = new List<int> { 11, 22 };
        var doctor = DoctorMother.Create(ids).Value;

        ids[0] = 99;
        ids.Clear();

        doctor.Specialties.Select(value => value.SpecialtyId).ShouldBe(new[] { 11, 22 }, ignoreOrder: true);
    }

    [Fact]
    public void Create_enumera_la_entrada_una_sola_vez()
    {
        var enumerations = 0;
        IEnumerable<int> Specialties()
        {
            enumerations++;
            enumerations.ShouldBe(1);
            yield return 11;
            yield return 22;
        }

        var doctor = DoctorMother.Create(Specialties()).Value;

        enumerations.ShouldBe(1);
        doctor.Specialties.Select(value => value.SpecialtyId).ShouldBe(new[] { 11, 22 }, ignoreOrder: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_rechaza_un_usuario_no_positivo(int id) =>
        AssertFailure(DoctorMother.Create([11], userId: id), DoctorErrors.InvalidUserId, "Doctor.InvalidUserId");

    public static IEnumerable<object?[]> InvalidText()
    {
        foreach (var field in new[] { "FirstName", "LastName", "LicenseNumber", "PhoneNumber", "AddressLine", "Biography" })
        {
            foreach (var value in new string?[] { null, "", " \t\r\n " })
                yield return [field, value, $"Doctor.{field}Required"];
            var maximum = field switch
            {
                "FirstName" or "LastName" => 100,
                "LicenseNumber" => 50,
                "PhoneNumber" => 20,
                "AddressLine" => 250,
                _ => 1000
            };
            yield return [field, new string('a', maximum + 1), $"Doctor.{field}TooLong"];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidText))]
    public void Create_valida_campos_profesionales_obligatorios_y_longitudes_del_esquema(string field, string? value, string code)
    {
        var result = field switch
        {
            "FirstName" => DoctorMother.Create([11], firstName: value),
            "LastName" => DoctorMother.Create([11], lastName: value),
            "LicenseNumber" => DoctorMother.Create([11], licenseNumber: value),
            "PhoneNumber" => DoctorMother.Create([11], phoneNumber: value),
            "AddressLine" => DoctorMother.Create([11], addressLine: value),
            _ => DoctorMother.Create([11], biography: value)
        };

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(code);
        result.Error.Description.ShouldNotBeNullOrWhiteSpace();
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rechaza_identificadores_geograficos_no_positivos(int id)
    {
        AssertFailure(DoctorMother.Create([11], provinceId: id), DoctorErrors.InvalidProvinceId, "Doctor.InvalidProvinceId");
        AssertFailure(DoctorMother.Create([11], municipalityId: id), DoctorErrors.InvalidMunicipalityId, "Doctor.InvalidMunicipalityId");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_rechaza_experiencia_negativa(int years) =>
        AssertFailure(DoctorMother.Create([11], yearsOfExperience: years), DoctorErrors.InvalidYearsOfExperience,
            "Doctor.InvalidYearsOfExperience");

    [Fact]
    public void Create_rechaza_tarifas_fuera_del_rango_numeric_10_2()
    {
        foreach (var fee in new[] { -0.01m, 100000000m, decimal.MinValue, decimal.MaxValue })
            AssertFailure(DoctorMother.Create([11], consultationFee: fee), DoctorErrors.InvalidConsultationFee,
                "Doctor.InvalidConsultationFee");
    }

    [Fact]
    public void Create_acepta_los_limites_del_perfil_despues_de_recortar_sin_alterar_su_contenido_interno()
    {
        var result = DoctorMother.Create([int.MaxValue], userId: int.MaxValue,
            firstName: " \t" + new string('a', 100) + " ",
            lastName: " " + new string('B', 100) + " ",
            licenseNumber: " " + new string('L', 50) + " ",
            phoneNumber: " " + new string('1', 20) + " ",
            provinceId: int.MaxValue, municipalityId: int.MaxValue,
            addressLine: " " + new string('d', 250) + " ",
            consultationFee: 99999999.99m, yearsOfExperience: int.MaxValue,
            biography: " " + new string('b', 1000) + "\r\n");

        result.IsSuccess.ShouldBeTrue();
        var doctor = result.Value;
        doctor.FirstName.ShouldBe(new string('a', 100));
        doctor.LastName.ShouldBe(new string('B', 100));
        doctor.LicenseNumber.ShouldBe(new string('L', 50));
        doctor.PhoneNumber.ShouldBe(new string('1', 20));
        doctor.AddressLine.ShouldBe(new string('d', 250));
        doctor.Biography.ShouldBe(new string('b', 1000));
        doctor.ConsultationFee.ShouldBe(99999999.99m);
        doctor.YearsOfExperience.ShouldBe(int.MaxValue);
        doctor.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(int.MaxValue);
    }

    [Fact]
    public void Create_admite_tarifa_y_experiencia_cero_y_recorta_sin_cambiar_mayusculas_o_espacios_internos()
    {
        var doctor = DoctorMother.Create([11], firstName: " Luis  José ", lastName: " PÉREZ ",
            licenseNumber: " LIC-227 ", phoneNumber: " 809-555-0100 ",
            addressLine: " Calle  Principal ", consultationFee: 0m, yearsOfExperience: 0,
            biography: " Atención  MÉDICA.\nPara la niñez. ").Value;

        doctor.FirstName.ShouldBe("Luis  José");
        doctor.LastName.ShouldBe("PÉREZ");
        doctor.LicenseNumber.ShouldBe(DoctorMother.LicenseNumber);
        doctor.PhoneNumber.ShouldBe(DoctorMother.PhoneNumber);
        doctor.AddressLine.ShouldBe("Calle  Principal");
        doctor.Biography.ShouldBe("Atención  MÉDICA.\nPara la niñez.");
        doctor.ConsultationFee.ShouldBe(0m);
        doctor.YearsOfExperience.ShouldBe(0);
    }

    private static void AssertFailure(Result<Doctor> result, Error expected, string code, ErrorType type = ErrorType.Validation)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(expected);
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
