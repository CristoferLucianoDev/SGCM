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
}
