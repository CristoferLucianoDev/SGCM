using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.People.Queries.GetDoctorProfile;

internal sealed class GetDoctorProfileQueryHandler(
    IDoctorProfileReader reader,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<GetDoctorProfileQuery> validator) : IQueryHandler<GetDoctorProfileQuery, DoctorProfileResult>
{
    public async Task<Result<DoctorProfileResult>> HandleAsync(
        GetDoctorProfileQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        if (currentUser.IsAuthenticated && currentUser.IsInRole(UserRole.Administrator)
            && currentUser.TryGetSessionIdentity(UserRole.Administrator, out var userId, out var sessionId)
            && userId > 0 && sessionId > 0)
        {
            var administrative = await reader.GetAdministrativeAsync(
                query.DoctorId, userId, sessionId, clock.UtcNow, cancellationToken);
            if (administrative is not null) return administrative;
        }

        var profile = await reader.GetPublicAsync(query.DoctorId, cancellationToken);
        return profile is null ? DoctorErrors.PublicProfileNotFound : Result<DoctorProfileResult>.Success(profile);
    }
}
