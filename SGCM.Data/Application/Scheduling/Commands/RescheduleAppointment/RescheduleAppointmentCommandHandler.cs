using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAppointment;

/// <summary>
/// Reprograma la cita del paciente autenticado. La propiedad se comprueba aquí porque el agregado no conoce al
/// usuario autenticado, y una cita ajena responde igual que una inexistente. Las reglas de la cita se validan primero
/// para informar el error de plazo antes que el de horario. Después, con los datos nuevos de la cita, se bloquea el
/// bloque del médico (así esta solicitud se ordena con las reservas simultáneas) y se comprueba que el horario siga
/// libre, sin contar la propia cita. Si no lo está se devuelve <see cref="AppointmentErrors.SlotUnavailable"/>; el
/// decorador revierte y no se publica el evento. Si aun así dos citas chocan, la base responde SlotUnavailable o
/// PatientOverlap.
/// </summary>
/// <remarks>
/// No comparte la comprobación de horario con RequestAppointmentCommandHandler: allí basta bloquear y verificar que no
/// haya ninguna cita, mientras que aquí se debe excluir la propia cita y comprobar con
/// <see cref="FreeSlotCalculator"/> que el horario cabe en un bloque abierto.
/// </remarks>
internal sealed class RescheduleAppointmentCommandHandler(
    ICurrentUser currentUser,
    IPatientRepository patientRepository,
    IAppointmentRepository appointmentRepository,
    IDoctorAvailabilityRepository availabilityRepository,
    IActiveAppointmentReader appointmentReader,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<RescheduleAppointmentCommand, Unit>
{
    // La ventana abarca una sola hora para que el único inicio posible sea el de la cita.
    private const int StartWindowHours = 1;

    public async Task<Result<Unit>> HandleAsync(
        RescheduleAppointmentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Patient))
            return AppointmentErrors.ReschedulePatientOnly;

        var patient = await patientRepository.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (patient is null) return PatientErrors.ProfileNotFound;

        var appointment = await appointmentRepository.GetByIdForUpdateAsync(command.AppointmentId, cancellationToken);
        if (appointment is null || appointment.PatientId != patient.Id) return AppointmentErrors.NotFound;

        var options = schedulingOptions.Value;
        var now = clock.UtcNow;
        var window = BookingWindows.ForPatient(now, calendar, options);
        // El validador garantiza que la nueva hora no es nula.
        var rescheduled = appointment.RescheduleByPatient(
            command.StartsAt!.Value, now, options.PatientRescheduleDeadlineHours, options.AppointmentDurationHours, window);
        if (rescheduled.IsFailure) return rescheduled.Error;

        var free = await IsScheduleFreeAsync(appointment, options.AppointmentDurationHours, cancellationToken);
        if (!free) return AppointmentErrors.SlotUnavailable;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }

    /// <summary>Bloquea el bloque abierto del médico y comprueba que el nuevo horario esté libre, sin contar la propia cita.</summary>
    private async Task<bool> IsScheduleFreeAsync(
        Appointment appointment, int durationHours, CancellationToken cancellationToken)
    {
        var (doctorId, startsAt, endsAt) = (appointment.DoctorId, appointment.StartsAt, appointment.EndsAt);
        if (!await availabilityRepository.LockOpenBlockAsync(doctorId, startsAt, endsAt, cancellationToken)) return false;

        var blocks = await availabilityRepository.ListByDoctorAsync(doctorId, startsAt, endsAt, cancellationToken);
        var openBlocks = blocks
            .Where(block => block.IsAvailable)
            .Select(block => TimeRange.Create(block.StartsAt, block.EndsAt).Value)
            .ToList();
        var booked = await appointmentReader.ListActiveIntervalsAsync([doctorId], startsAt, endsAt, cancellationToken);
        IReadOnlyList<BookedInterval> activeAppointments = booked.GetValueOrDefault(doctorId) ?? [];
        var startWindow = TimeRange.Create(startsAt, startsAt.AddHours(StartWindowHours)).Value;

        return FreeSlotCalculator
            .Calculate(openBlocks, activeAppointments, startWindow, durationHours, appointment.Id)
            .Contains(startsAt);
    }
}
