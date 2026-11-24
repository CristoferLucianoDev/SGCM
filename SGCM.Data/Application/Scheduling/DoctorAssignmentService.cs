using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Implementación de <see cref="IDoctorAssignmentService"/>. Vive en Application porque cruza los agregados
/// Doctor y DoctorAvailability; el horario libre se calcula con <see cref="FreeSlotCalculator"/>, igual que en
/// la consulta de horarios libres, y el azar llega como dependencia.
/// </summary>
internal sealed class DoctorAssignmentService(
    IActiveDoctorReader doctorReader,
    IDoctorAvailabilityRepository availabilityRepository,
    IActiveAppointmentReader appointmentReader,
    IRandomNumberGenerator random,
    IOptions<SchedulingOptions> schedulingOptions) : IDoctorAssignmentService
{
    public async Task<Result<int>> AssignAsync(
        int specialtyId, DateTimeOffset startsAt, int? provinceId, int? municipalityId, CancellationToken cancellationToken)
    {
        if (!HourAlignedInstant.IsOnHour(startsAt)) return AppointmentErrors.NotOnHour;

        var candidates = await doctorReader.ListActiveDoctorIdsBySpecialtyAsync(
            specialtyId, provinceId, municipalityId, cancellationToken);
        if (candidates.Count == 0) return AppointmentErrors.SlotUnavailable;

        var freeDoctors = await ListFreeDoctorsAsync(candidates, startsAt.ToUniversalTime(), cancellationToken);
        if (freeDoctors.Count == 0) return AppointmentErrors.SlotUnavailable;

        return freeDoctors[random.Next(freeDoctors.Count)];
    }

    /// <summary>Médicos, en el orden recibido, en los que cabe una cita completa que empieza en ese instante.</summary>
    private async Task<IReadOnlyList<int>> ListFreeDoctorsAsync(
        IReadOnlyList<int> doctorIds, DateTimeOffset startsAt, CancellationToken cancellationToken)
    {
        var durationHours = schedulingOptions.Value.AppointmentDurationHours;
        var endsAt = startsAt.AddHours(durationHours);

        var blocks = await availabilityRepository.ListByDoctorsAsync(doctorIds, startsAt, endsAt, cancellationToken);
        var openBlocksByDoctor = blocks
            .Where(block => block.IsAvailable)
            .ToLookup(block => block.DoctorId, block => TimeRange.Create(block.StartsAt, block.EndsAt).Value);
        var appointmentsByDoctor = await appointmentReader.ListActiveIntervalsAsync(
            doctorIds, startsAt, endsAt, cancellationToken);

        // Una ventana de una hora que empieza en el horario solo admite ese inicio como candidato.
        var window = TimeRange.Create(startsAt, startsAt.AddHours(1)).Value;

        return doctorIds
            .Where(doctorId => FreeSlotCalculator.Calculate(
                    openBlocksByDoctor[doctorId],
                    appointmentsByDoctor.GetValueOrDefault(doctorId) ?? [],
                    window,
                    durationHours)
                .Contains(startsAt))
            .ToList();
    }
}
