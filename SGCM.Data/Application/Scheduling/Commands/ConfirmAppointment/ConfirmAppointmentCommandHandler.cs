using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;

/// <summary>
/// Confirma la cita del médico autenticado. La cita propia y bloqueada la entrega <see cref="DoctorAppointmentAccess"/>,
/// así que dos decisiones simultáneas no pasan ambas: la segunda la encuentra ya confirmada y recibe
/// <see cref="AppointmentErrors.NotPending"/>.
/// </summary>
internal sealed class ConfirmAppointmentCommandHandler(
    DoctorAppointmentAccess access,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<ConfirmAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ConfirmAppointmentCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(
            command.AppointmentId, AppointmentErrors.DecisionDoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var confirmed = found.Value.Confirm(clock.UtcNow, schedulingOptions.Value.ConfirmationDeadlineHours);
        if (confirmed.IsFailure) return confirmed.Error;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }
}
