using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;

/// <summary>
/// Resuelve el detalle de una cita del paciente autenticado. El paciente sale siempre del usuario autenticado y
/// una cita ajena responde igual que una inexistente, para no revelar que existe.
/// </summary>
internal sealed class GetPatientAppointmentDetailQueryHandler(
    ICurrentUser currentUser,
    IPatientRepository patientRepository,
    IPatientAppointmentHistoryReader reader,
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

        return detail;
    }
}
