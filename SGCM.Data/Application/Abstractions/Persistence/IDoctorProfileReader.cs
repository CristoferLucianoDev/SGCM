using SGCM.Data.Application.People.Queries.GetDoctorProfile;

namespace SGCM.Data.Application.Abstractions.Persistence;

internal interface IDoctorProfileReader
{
    Task<DoctorPublicProfileDto?> GetPublicAsync(int doctorId, CancellationToken cancellationToken);

    Task<DoctorAdminProfileDto?> GetAdministrativeAsync(
        int doctorId, int viewerUserId, int viewerSessionId, DateTimeOffset now, CancellationToken cancellationToken);
}
