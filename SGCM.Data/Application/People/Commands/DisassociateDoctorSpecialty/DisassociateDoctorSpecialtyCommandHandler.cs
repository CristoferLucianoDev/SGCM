using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;

internal sealed class DisassociateDoctorSpecialtyCommandHandler(
    ICurrentUser currentUser,
    IDoctorRepository doctorRepository) : ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DisassociateDoctorSpecialtyCommand command, CancellationToken cancellationToken)
    {
        var doctor = await doctorRepository.GetByUserIdForUpdateAsync(currentUser.UserId, cancellationToken);
        if (doctor is null) return DoctorErrors.NotFound;
        // La materialización de datos legados no pasa por Doctor.Create.
        if (doctor.Specialties.Count == 0) return DoctorErrors.ProfileIncomplete;

        var result = doctor.DisassociateSpecialty(command.SpecialtyId);
        if (result.IsFailure) return result.Error;
        return Unit.Value;
    }
}
