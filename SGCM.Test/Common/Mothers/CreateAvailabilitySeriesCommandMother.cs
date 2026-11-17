using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="CreateAvailabilitySeriesCommand"/>, con los mismos datos de
/// <see cref="DoctorAvailabilitySeriesMother"/>: lunes, miércoles y viernes de 8:00 a 14:00 desde el 4 de enero de 2027.
/// </summary>
public static class CreateAvailabilitySeriesCommandMother
{
    /// <summary>Lunes, miércoles y viernes en formato ISO 8601.</summary>
    public static readonly int[] IsoDaysOfWeek = [1, 3, 5];

    /// <summary>Días hacia adelante hasta los que se generan bloques, como en appsettings.json.</summary>
    public const int BookingHorizonDays = 90;

    /// <summary>
    /// Configuración de agenda de las pruebas: Santo Domingo, bloques de al menos 6 horas y horizonte de 90 días.
    /// </summary>
    public static SchedulingOptions Options() => new()
    {
        TimeZone = DoctorAvailabilitySeriesMother.TimeZone,
        MinimumBlockDurationHours = DoctorAvailabilitySeriesMother.MinimumDurationHours,
        BookingHorizonDays = BookingHorizonDays
    };

    public static CreateAvailabilitySeriesCommand Valid() => new(
        IsoDaysOfWeek,
        DoctorAvailabilitySeriesMother.StartTime,
        DoctorAvailabilitySeriesMother.EndTime,
        DoctorAvailabilitySeriesMother.StartDate,
        EndDate: null);
}
