using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;

internal sealed class AssociateDoctorSpecialtyCommandHandler(
    ICurrentUser currentUser,
    IDoctorRepository doctorRepository,
    ISpecialtyRepository specialtyRepository) : ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AssociateDoctorSpecialtyCommand command, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(currentUser.UserId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;
        // La materialización de datos legados no pasa por Doctor.Create.
        if (doctor.Specialties.Count == 0) return DoctorErrors.ProfileIncomplete;

        var specialty = await specialtyRepository.GetByIdAsync(command.SpecialtyId, cancellationToken);
        if (specialty is null) return SpecialtyErrors.NotFound;
        if (!specialty.IsActive) return SpecialtyErrors.Inactive;

        var result = doctor.AssociateSpecialty(command.SpecialtyId);
        if (result.IsFailure) return result.Error;
        return Unit.Value;
    }
}
