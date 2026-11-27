using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;

/// <summary>
/// Devuelve solo las citas del médico de la sesión; un usuario sin perfil de médico activo no tiene agenda.
/// </summary>
internal sealed class GetDoctorScheduleQueryHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IDoctorScheduleReader reader,
    IValidator<GetDoctorScheduleQuery> validator) : IQueryHandler<GetDoctorScheduleQuery, PagedResult<DoctorScheduleItemDto>>
{
    public async Task<Result<PagedResult<DoctorScheduleItemDto>>> HandleAsync(
        GetDoctorScheduleQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor)) return DoctorErrors.NotFound;
        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        var patientName = string.IsNullOrWhiteSpace(query.PatientName) ? null : query.PatientName.Trim();
        var filter = new DoctorScheduleFilter(query.Status, query.From, query.To, patientName, query.Sort);

        return await reader.ListAsync(doctorId.Value, filter, query.PageNumber, query.PageSize, cancellationToken);
    }
}
