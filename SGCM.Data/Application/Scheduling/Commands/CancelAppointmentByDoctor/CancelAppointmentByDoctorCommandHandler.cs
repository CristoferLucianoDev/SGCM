using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;

/// <summary>
/// Cancela la cita del médico autenticado. La cita propia y bloqueada la entrega <see cref="DoctorAppointmentAccess"/>,
/// así que dos cancelaciones simultáneas no pasan ambas: la segunda la encuentra cancelada y recibe
/// <see cref="AppointmentErrors.InvalidTransition"/>.
/// </summary>
internal sealed class CancelAppointmentByDoctorCommandHandler(
    DoctorAppointmentAccess access,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<CancelAppointmentByDoctorCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        CancelAppointmentByDoctorCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(
            command.AppointmentId, AppointmentErrors.CancellationDoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var appointment = found.Value;
        var options = schedulingOptions.Value;
        var cancelled = appointment.Cancel(
            AppointmentCanceller.Doctor,
            command.Reason,
            clock.UtcNow,
            options.PatientCancellationDeadlineHours,
            options.ToShortNoticeCancellation());
        if (cancelled.IsFailure) return cancelled.Error;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }
}
