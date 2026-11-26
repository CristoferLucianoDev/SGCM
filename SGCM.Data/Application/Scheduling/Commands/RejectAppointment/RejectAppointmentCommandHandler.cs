using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.RejectAppointment;

/// <summary>
/// Rechaza la cita del médico autenticado. La cita propia y bloqueada la entrega <see cref="DoctorAppointmentAccess"/>,
/// así que dos decisiones simultáneas no pasan ambas: la segunda la encuentra ya rechazada y recibe
/// <see cref="AppointmentErrors.NotPending"/>.
/// </summary>
internal sealed class RejectAppointmentCommandHandler(
    DoctorAppointmentAccess access,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<RejectAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RejectAppointmentCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(
            command.AppointmentId, AppointmentErrors.DecisionDoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var rejected = found.Value.Reject(
            command.Reason, clock.UtcNow, schedulingOptions.Value.ConfirmationDeadlineHours);
        if (rejected.IsFailure) return rejected.Error;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }
}
