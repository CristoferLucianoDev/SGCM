using SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetAvailableSlotsQueryValidatorTests
{
    private readonly GetAvailableSlotsQueryValidator _validator = new();

    [Fact]
    public void Un_DoctorId_nulo_es_valido()
    {
        new GetAvailableSlotsQuery().DoctorId.ShouldBeNull();
        _validator.Validate(new GetAvailableSlotsQuery()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Un_DoctorId_positivo_es_valido(int doctorId)
    {
        _validator.Validate(new GetAvailableSlotsQuery(doctorId)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Un_DoctorId_no_positivo_se_rechaza(int doctorId)
    {
        var result = _validator.Validate(new GetAvailableSlotsQuery(doctorId));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.ErrorCode.ShouldBe("AvailableSlots.InvalidDoctorId");
        failure.PropertyName.ShouldBe(nameof(GetAvailableSlotsQuery.DoctorId));
    }

    [Fact]
    public void SpecialtyId_ProvinceId_y_MunicipalityId_nulos_son_validos()
    {
        var query = new GetAvailableSlotsQuery();

        query.SpecialtyId.ShouldBeNull();
        query.ProvinceId.ShouldBeNull();
        query.MunicipalityId.ShouldBeNull();
        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void SpecialtyId_ProvinceId_y_MunicipalityId_con_valor_uno_son_validos()
    {
        _validator.Validate(new GetAvailableSlotsQuery(SpecialtyId: 1, ProvinceId: 1, MunicipalityId: 1))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_SpecialtyId_no_positivo_se_rechaza(int specialtyId)
    {
        var failure = _validator.Validate(new GetAvailableSlotsQuery(SpecialtyId: specialtyId))
            .Errors.ShouldHaveSingleItem();

        failure.ErrorCode.ShouldBe("AvailableSlots.InvalidSpecialtyId");
        failure.PropertyName.ShouldBe(nameof(GetAvailableSlotsQuery.SpecialtyId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_ProvinceId_no_positivo_se_rechaza(int provinceId)
    {
        var failure = _validator.Validate(new GetAvailableSlotsQuery(ProvinceId: provinceId))
            .Errors.ShouldHaveSingleItem();

        failure.ErrorCode.ShouldBe("AvailableSlots.InvalidProvinceId");
        failure.PropertyName.ShouldBe(nameof(GetAvailableSlotsQuery.ProvinceId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_MunicipalityId_no_positivo_se_rechaza(int municipalityId)
    {
        var failure = _validator.Validate(new GetAvailableSlotsQuery(MunicipalityId: municipalityId))
            .Errors.ShouldHaveSingleItem();

        failure.ErrorCode.ShouldBe("AvailableSlots.InvalidMunicipalityId");
        failure.PropertyName.ShouldBe(nameof(GetAvailableSlotsQuery.MunicipalityId));
    }
}
