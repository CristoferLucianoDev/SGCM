using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorCalendar;

/// <summary>Devuelve todas las citas activas del médico de la sesión en el rango, en orden cronológico.</summary>
internal sealed class GetDoctorCalendarQueryHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IDoctorScheduleReader reader,
    IValidator<GetDoctorCalendarQuery> validator) : IQueryHandler<GetDoctorCalendarQuery, IReadOnlyList<DoctorScheduleItemDto>>
{
    public async Task<Result<IReadOnlyList<DoctorScheduleItemDto>>> HandleAsync(
        GetDoctorCalendarQuery query, CancellationToken cancellationToken)
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

        var items = await reader.ListRangeAsync(doctorId.Value, query.From, query.To, cancellationToken);
        return Result<IReadOnlyList<DoctorScheduleItemDto>>.Success(items);
    }
}
