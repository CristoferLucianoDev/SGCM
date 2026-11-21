using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.People.Commands.UpdateDoctorProfile;

internal sealed class UpdateDoctorProfileCommandHandler(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    IDoctorRepository doctorRepository,
    IDoctorProfileCatalogReader catalogReader,
    IDateTimeProvider clock) : ICommandHandler<UpdateDoctorProfileCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateDoctorProfileCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetSessionIdentity(out var userId, out var sessionId))
            return UserErrors.SessionInvalid;

        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);
        if (user is null) return UserErrors.SessionInvalid;
        if (!user.IsActive) return UserErrors.AccountInactive;
        if (!user.IsEmailConfirmed) return UserErrors.EmailNotConfirmed;
        if (user.Role != UserRole.Doctor) return DoctorErrors.RoleRequired;

        var now = clock.UtcNow;
        if (!user.Sessions.Any(session => session.Id == sessionId && session.IsActiveAt(now)))
            return UserErrors.SessionInvalid;
        if (user.MustChangePassword) return DoctorErrors.TemporaryPasswordChangeRequired;

        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(userId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;

        var location = await catalogReader.GetLocationAsync(command.ProvinceId, command.MunicipalityId, cancellationToken);
        if (!location.ProvinceExists) return DoctorErrors.ProvinceNotFound;
        if (location.MunicipalityProvinceId is null) return DoctorErrors.MunicipalityNotFound;
        if (location.MunicipalityProvinceId != command.ProvinceId) return DoctorErrors.MunicipalityProvinceMismatch;

        // El último reemplazo confirmado bajo el bloqueo deja su conjunto completo de datos editables.
        var updated = doctor.UpdateProfessionalData(command.PhoneNumber, command.ProvinceId, command.MunicipalityId,
            command.AddressLine, command.ConsultationFee, command.YearsOfExperience, command.Biography);
        return updated.IsSuccess ? Unit.Value : updated.Error;
    }
}
