using FluentValidation;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;

/// <summary>
/// Resuelve el detalle de una cita del paciente autenticado. El paciente sale siempre del usuario autenticado y
/// una cita ajena responde igual que una inexistente, para no revelar que existe. Las acciones permitidas las decide
/// el agregado, no el lector, así que la cita se carga una vez que el lector confirmó que es del paciente.
/// </summary>
internal sealed class GetPatientAppointmentDetailQueryHandler(
    ICurrentUser currentUser,
    IPatientRepository patientRepository,
    IPatientAppointmentHistoryReader reader,
    IAppointmentRepository appointmentRepository,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions,
    IValidator<GetPatientAppointmentDetailQuery> validator)
    : IQueryHandler<GetPatientAppointmentDetailQuery, PatientAppointmentDetailDto>
{
    public async Task<Result<PatientAppointmentDetailDto>> HandleAsync(
        GetPatientAppointmentDetailQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Patient))
            return AppointmentErrors.HistoryPatientOnly;

        var patient = await patientRepository.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (patient is null) return PatientErrors.ProfileNotFound;

        var detail = await reader.GetAsync(patient.Id, query.AppointmentId, cancellationToken);
        if (detail is null) return AppointmentErrors.NotFound;

        // Solo falla si la cita desaparece entre las dos lecturas.
        var appointment = await appointmentRepository.GetByIdAsync(query.AppointmentId, cancellationToken);
        if (appointment is null) return AppointmentErrors.NotFound;

        var allowedActions = appointment.GetAllowedActions(
            AppointmentParticipant.Patient, clock.UtcNow, schedulingOptions.Value.ToAppointmentRules());

        return new PatientAppointmentDetailDto(
            detail.Appointment, detail.ReasonForVisit, detail.StatusReason, allowedActions, detail.DoctorProfile);
    }
}
