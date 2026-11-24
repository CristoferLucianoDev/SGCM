using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Implementación de <see cref="IAffectedAppointmentsCanceler"/>: encuentra las citas con la política de citas
/// afectadas, bloquea cada una y la cancela con <see cref="Appointment.Cancel"/> como cancelación del médico y el
/// motivo común. Cada cita emite <see cref="Domain.Scheduling.Appointments.Events.AppointmentCancelledEvent"/>, y el
/// correo al paciente sale después del commit con el manejador que ya existe.
/// </summary>
internal sealed class AffectedAppointmentsCanceler(
    IAffectedAppointmentsPolicy policy,
    IAppointmentRepository appointmentRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : IAffectedAppointmentsCanceler
{
    public async Task<Result<int>> CancelAsync(
        int doctorId, IReadOnlyCollection<TimeRange> removedRanges, string? reason, CancellationToken cancellationToken)
    {
        var affected = await policy.EvaluateAsync(doctorId, removedRanges, cancellationToken);
        if (affected.Appointments.Count == 0) return 0;

        var options = schedulingOptions.Value;
        var now = clock.UtcNow;

        foreach (var booked in affected.Appointments)
        {
            // La cita estaba activa al leerla; si otra transacción la cambió, se lee bloqueada y Cancel decide.
            var appointment = await appointmentRepository.GetByIdForUpdateAsync(booked.AppointmentId, cancellationToken);
            if (appointment is null) continue;

            var cancelled = appointment.Cancel(
                AppointmentCanceller.Doctor,
                reason,
                now,
                options.PatientCancellationDeadlineHours,
                affected.ShortNotice);
            if (cancelled.IsFailure) return cancelled.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return affected.Appointments.Count;
    }
}
