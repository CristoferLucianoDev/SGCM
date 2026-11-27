using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAppointmentByDoctor;

/// <summary>
/// Reprograma la cita del médico autenticado. La cita propia y bloqueada la entrega <see cref="DoctorAppointmentAccess"/>,
/// que comprueba la propiedad y responde igual para una cita ajena que para una inexistente. Las reglas de la cita se
/// validan primero, con la ventana del médico, para informar sus errores antes que el de horario. Después, con los
/// datos nuevos de la cita, <see cref="AppointmentSlotChecker"/> bloquea el bloque y comprueba que el horario esté libre
/// en los bloques propios del médico, sin contar la propia cita. Si no lo está se devuelve
/// <see cref="AppointmentErrors.SlotUnavailable"/>; el decorador revierte y no sale el aviso al paciente. Si aun así
/// dos citas chocan, la base responde SlotUnavailable o PatientOverlap.
/// </summary>
internal sealed class RescheduleAppointmentByDoctorCommandHandler(
    DoctorAppointmentAccess access,
    AppointmentSlotChecker slotChecker,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<RescheduleAppointmentByDoctorCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RescheduleAppointmentByDoctorCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(
            command.AppointmentId, AppointmentErrors.RescheduleDoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var appointment = found.Value;
        var options = schedulingOptions.Value;
        var now = clock.UtcNow;
        var window = BookingWindows.ForDoctor(now, calendar, options);
        // El validador garantiza que la nueva hora no es nula.
        var rescheduled = appointment.RescheduleByDoctor(
            command.StartsAt!.Value, now, options.AppointmentDurationHours, window);
        if (rescheduled.IsFailure) return rescheduled.Error;

        var free = await slotChecker.IsFreeForAsync(appointment, options.AppointmentDurationHours, cancellationToken);
        if (!free) return AppointmentErrors.SlotUnavailable;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }
}
