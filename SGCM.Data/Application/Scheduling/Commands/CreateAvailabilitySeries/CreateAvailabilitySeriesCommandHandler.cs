using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;

/// <summary>
/// Crea la serie bajo el bloqueo del perfil del médico, para que dos solicitudes simultáneas del mismo
/// médico no pasen a la vez la comprobación de traslapes. Los bloques los genera el manejador del evento
/// de serie creada, que se despacha al confirmar.
/// </summary>
internal sealed class CreateAvailabilitySeriesCommandHandler(
    ICurrentUser currentUser,
    IDoctorRepository doctorRepository,
    IDoctorAvailabilitySeriesRepository seriesRepository,
    IDoctorAvailabilityRepository availabilityRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<CreateAvailabilitySeriesCommand, int>
{
    public async Task<Result<int>> HandleAsync(CreateAvailabilitySeriesCommand command, CancellationToken cancellationToken)
    {
        var options = schedulingOptions.Value;

        var days = ToDaysOfWeek(command.DaysOfWeek);
        if (days.IsFailure) return days.Error;

        if (command.StartDate < Today(options.TimeZone)) return DoctorAvailabilitySeriesErrors.StartDateInPast;

        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(currentUser.UserId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;

        var created = DoctorAvailabilitySeries.Create(
            doctor.Id,
            days.Value,
            command.StartTime,
            command.EndTime,
            command.StartDate,
            command.EndDate,
            options.TimeZone,
            options.MinimumBlockDurationHours);
        if (created.IsFailure) return created.Error;
        var series = created.Value;

        var activeSeries = await seriesRepository.ListActiveByDoctorAsync(doctor.Id, cancellationToken);
        if (activeSeries.Any(other => series.OverlapsWith(other))) return DoctorAvailabilitySeriesErrors.Overlap;

        // Un día de margen a cada lado de la vigencia cubre la diferencia entre UTC y la zona de la serie.
        var from = new DateTimeOffset(series.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset? to = series.EndDate is { } endDate
            ? new DateTimeOffset(endDate.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        var blocks = await availabilityRepository.ListByDoctorAsync(doctor.Id, from, to, cancellationToken);
        if (blocks.Any(block => series.OverlapsWith(block.StartsAt, block.EndsAt)))
            return DoctorAvailabilitySeriesErrors.Overlap;

        seriesRepository.Add(series);
        // El ID se genera al guardar; el decorador confirma la transacción.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return series.Id;
    }

    /// <summary>
    /// Convierte los días ISO 8601 del comando; una lista vacía la rechaza después el dominio.
    /// </summary>
    internal static Result<DayOfWeek[]> ToDaysOfWeek(IReadOnlyList<int>? isoDays)
    {
        var days = new List<DayOfWeek>();
        foreach (var isoDay in isoDays ?? [])
        {
            if (!DoctorAvailabilitySeries.TryFromIsoDayNumber(isoDay, out var day))
                return DoctorAvailabilitySeriesErrors.InvalidDayOfWeek;
            days.Add(day);
        }

        return days.ToArray();
    }

    // La fecha de hoy en la zona horaria de la agenda, no en UTC: a las 21:00 de Santo Domingo ya es mañana en UTC.
    private DateOnly Today(string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);
}
