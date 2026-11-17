using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AvailabilityMapperTests
{
    [Fact]
    public void La_solicitud_se_mapea_al_comando_sin_cambiar_los_datos()
    {
        var request = new CreateAvailabilitySeriesRequest(
            [7, 1], new TimeOnly(8, 0), new TimeOnly(14, 0), new DateOnly(2027, 1, 4), null);

        var command = request.ToCommand();

        command.DaysOfWeek!.ShouldBe(new[] { 7, 1 });
        command.StartTime.ShouldBe(new TimeOnly(8, 0));
        command.EndTime.ShouldBe(new TimeOnly(14, 0));
        command.StartDate.ShouldBe(new DateOnly(2027, 1, 4));
        command.EndDate.ShouldBeNull();
    }

    [Fact]
    public void La_solicitud_de_bloque_se_mapea_al_comando_sin_cambiar_los_datos()
    {
        var command = new CreateAvailabilityBlockRequest(new DateOnly(2027, 1, 5), new TimeOnly(8, 0), new TimeOnly(14, 0))
            .ToCommand();

        command.Date.ShouldBe(new DateOnly(2027, 1, 5));
        command.StartTime.ShouldBe(new TimeOnly(8, 0));
        command.EndTime.ShouldBe(new TimeOnly(14, 0));
    }
}
