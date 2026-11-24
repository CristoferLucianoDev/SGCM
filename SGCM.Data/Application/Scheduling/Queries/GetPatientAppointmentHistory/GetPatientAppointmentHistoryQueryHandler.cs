using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;

/// <summary>
/// Resuelve el historial de citas del paciente autenticado. El paciente sale siempre del usuario autenticado,
/// así que nunca ve citas de otro.
/// </summary>
internal sealed class GetPatientAppointmentHistoryQueryHandler(
    ICurrentUser currentUser,
    IPatientRepository patientRepository,
    IPatientAppointmentHistoryReader reader,
    IValidator<GetPatientAppointmentHistoryQuery> validator)
    : IQueryHandler<GetPatientAppointmentHistoryQuery, PagedResult<PatientAppointmentDto>>
{
    public async Task<Result<PagedResult<PatientAppointmentDto>>> HandleAsync(
        GetPatientAppointmentHistoryQuery query, CancellationToken cancellationToken)
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

        return await reader.ListAsync(
            patient.Id, query.Status, query.From, query.To, query.PageNumber, query.PageSize, cancellationToken);
    }
}
