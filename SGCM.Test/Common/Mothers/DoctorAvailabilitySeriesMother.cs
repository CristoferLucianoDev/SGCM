using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="DoctorAvailabilitySeries"/>: lunes, miércoles y viernes de 8:00 a 14:00
/// en Santo Domingo, desde el lunes 4 de enero de 2027 y sin fecha hasta.
/// </summary>
public static class DoctorAvailabilitySeriesMother
{
    public const int DoctorId = 7;
    public const string TimeZone = "America/Santo_Domingo";
    public const int MinimumDurationHours = 6;

    public static readonly DayOfWeek[] DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
    public static readonly TimeOnly StartTime = new(8, 0);
    public static readonly TimeOnly EndTime = new(14, 0);
    public static readonly DateOnly StartDate = new(2027, 1, 4);

    public static Result<DoctorAvailabilitySeries> Create(
        IEnumerable<DayOfWeek>? daysOfWeek = null,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? timeZone = TimeZone,
        int doctorId = DoctorId,
        int minimumDurationHours = MinimumDurationHours) =>
        DoctorAvailabilitySeries.Create(
            doctorId,
            daysOfWeek ?? DaysOfWeek,
            startTime ?? StartTime,
            endTime ?? EndTime,
            startDate ?? StartDate,
            endDate,
            timeZone,
            minimumDurationHours);

    /// <summary>Serie válida, para las pruebas que no validan su creación.</summary>
    public static DoctorAvailabilitySeries Valid(
        IEnumerable<DayOfWeek>? daysOfWeek = null,
        DateOnly? endDate = null) =>
        Create(daysOfWeek, endDate: endDate).Value;
}
