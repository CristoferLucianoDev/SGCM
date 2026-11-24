using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.CancelAppointment;

/// <summary>
/// Cancela la cita del paciente autenticado. La propiedad se comprueba aquí porque el agregado no conoce al
/// usuario autenticado, y una cita ajena responde igual que una inexistente para no revelar citas de otros.
/// La cita se bloquea para que dos cancelaciones simultáneas no pasen ambas: la segunda la encuentra
/// cancelada y recibe <see cref="AppointmentErrors.InvalidTransition"/>.
/// </summary>
internal sealed class CancelAppointmentCommandHandler(
    ICurrentUser currentUser,
    IPatientRepository patientRepository,
    IAppointmentRepository appointmentRepository,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<CancelAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CancelAppointmentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Patient))
            return AppointmentErrors.CancellationPatientOnly;

        var patient = await patientRepository.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (patient is null) return PatientErrors.ProfileNotFound;

        var appointment = await appointmentRepository.GetByIdForUpdateAsync(command.AppointmentId, cancellationToken);
        if (appointment is null || appointment.PatientId != patient.Id) return AppointmentErrors.NotFound;

        var cancelled = appointment.Cancel(
            AppointmentCanceller.Patient,
            command.Reason,
            clock.UtcNow,
            schedulingOptions.Value.PatientCancellationDeadlineHours);
        if (cancelled.IsFailure) return cancelled.Error;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }
}
