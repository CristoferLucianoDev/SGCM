using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.CompleteDoctorProfile;

internal sealed class CompleteDoctorProfileCommandHandler(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    IUserProfileReader profileReader,
    IDoctorProfileCatalogReader catalogReader,
    IDoctorRepository doctorRepository,
    ISessionService sessionService,
    IDateTimeProvider clock) : ICommandHandler<CompleteDoctorProfileCommand, SessionTokens>
{
    public async Task<Result<SessionTokens>> HandleAsync(
        CompleteDoctorProfileCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetSessionIdentity(out var userId, out var sessionId))
            return UserErrors.SessionInvalid;

        // Bloquea primero al usuario para serializar las altas y los cambios de sesión de la cuenta.
        var user = await userRepository.GetByIdForUpdateAsync(userId, cancellationToken);
        if (user is null) return UserErrors.SessionInvalid;
        if (!user.IsActive) return UserErrors.AccountInactive;
        if (!user.IsEmailConfirmed) return UserErrors.EmailNotConfirmed;
        if (user.Role != UserRole.Doctor) return DoctorErrors.RoleRequired;

        var now = clock.UtcNow;
        if (!user.Sessions.Any(session => session.Id == sessionId && session.IsActiveAt(now)))
            return UserErrors.SessionInvalid;
        if (user.MustChangePassword) return DoctorErrors.TemporaryPasswordChangeRequired;

        if (await profileReader.HasProfileAsync(userId, UserRole.Doctor, cancellationToken))
            return DoctorErrors.ProfileAlreadyExists;

        var created = command.CreateDoctor(userId);
        if (created.IsFailure) return created.Error;
        var doctor = created.Value;

        var location = await catalogReader.GetLocationAsync(doctor.ProvinceId, doctor.MunicipalityId, cancellationToken);
        if (!location.ProvinceExists) return DoctorErrors.ProvinceNotFound;
        if (location.MunicipalityProvinceId is null) return DoctorErrors.MunicipalityNotFound;
        if (location.MunicipalityProvinceId != doctor.ProvinceId) return DoctorErrors.MunicipalityProvinceMismatch;

        var specialtyIds = doctor.Specialties.Select(link => link.SpecialtyId).ToArray();
        var specialties = await catalogReader.GetSpecialtiesAsync(specialtyIds, cancellationToken);
        if (!specialtyIds.ToHashSet().SetEquals(specialties.Select(specialty => specialty.Id)))
            return SpecialtyErrors.NotFound;
        if (specialties.Any(specialty => !specialty.IsActive)) return SpecialtyErrors.Inactive;

        if (await doctorRepository.ExistsByLicenseNumberAsync(doctor.LicenseNumber, cancellationToken))
            return DoctorErrors.LicenseNumberAlreadyInUse;

        doctorRepository.Add(doctor);
        // StartAsync guarda perfil y sesión antes de releer el paso pendiente. El decorador confirma ambos.
        return await sessionService.StartAsync(user, command.DeviceName, command.IpAddress, cancellationToken);
    }
}
