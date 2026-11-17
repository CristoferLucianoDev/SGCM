using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Web.Contracts.Scheduling;

namespace SGCM.Web.Mappers.Scheduling;

public static class AvailabilityMapper
{
    public static CreateAvailabilitySeriesCommand ToCommand(this CreateAvailabilitySeriesRequest request) =>
        new(request.DaysOfWeek, request.StartTime, request.EndTime, request.StartDate, request.EndDate);

    public static CreateAvailabilityBlockCommand ToCommand(this CreateAvailabilityBlockRequest request) =>
        new(request.Date, request.StartTime, request.EndTime);
}
