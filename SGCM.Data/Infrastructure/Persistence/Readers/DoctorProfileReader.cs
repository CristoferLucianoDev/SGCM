using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class DoctorProfileReader(AppDbContext context) : IDoctorProfileReader
{
    public Task<DoctorPublicProfileDto?> GetPublicAsync(int doctorId, CancellationToken cancellationToken) =>
        DoctorPublicProfileProjection.GetAsync(context, doctorId, cancellationToken);

    public async Task<DoctorAdminProfileDto?> GetAdministrativeAsync(
        int doctorId, int viewerUserId, int viewerSessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var authorizedDetails = context.Database.SqlQuery<PrivateProfileRow>($"""
            SELECT doctor.id, doctor.phone_number, doctor.license_number
            FROM people.doctor doctor
            WHERE doctor.id = {doctorId}
              AND EXISTS (
                  SELECT 1
                  FROM app."user" viewer
                  JOIN app.user_session session ON session.user_id = viewer.id
                  WHERE viewer.id = {viewerUserId}
                    AND viewer.is_active
                    AND viewer.is_email_confirmed
                    AND viewer.role_id = {(int)UserRole.Administrator}
                    AND NOT viewer.must_change_password
                    AND session.id = {viewerSessionId}
                    AND session.revoked_at IS NULL
                    AND session.expires_at > {now}
              )
            """);

        var row = await DoctorPublicProfileProjection.Query(context, doctorId)
            .Join(authorizedDetails, profile => profile.Id, details => details.Id, (profile, details) => new { profile, details })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new DoctorAdminProfileDto(
            DoctorPublicProfileProjection.Map(row.profile), row.details.PhoneNumber, row.details.LicenseNumber);
    }

    private sealed record PrivateProfileRow(int Id, string PhoneNumber, string LicenseNumber);
}
