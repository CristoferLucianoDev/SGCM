using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class DoctorProfileReader(AppDbContext context) : IDoctorProfileReader
{
    public async Task<DoctorPublicProfileDto?> GetPublicAsync(int doctorId, CancellationToken cancellationToken)
    {
        var row = await DirectoryProfile(doctorId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : MapPublicProfile(row);
    }

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

        var row = await DirectoryProfile(doctorId)
            .Join(authorizedDetails, profile => profile.Id, details => details.Id, (profile, details) => new { profile, details })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new DoctorAdminProfileDto(
            MapPublicProfile(row.profile), row.details.PhoneNumber, row.details.LicenseNumber);
    }

    private IQueryable<DoctorSearchDto> DirectoryProfile(int doctorId) =>
        context.Database.SqlQuery<DoctorSearchDto>($"""
            SELECT id, first_name, last_name, province_id, province_name, municipality_id, municipality_name,
                   address_line, consultation_fee, years_of_experience, biography, specialty_ids, specialty_names
            FROM people.vw_doctor_directory
            WHERE id = {doctorId}
            """);

    private static DoctorPublicProfileDto MapPublicProfile(DoctorSearchDto row) => new(
        row.Id,
        $"{row.FirstName.Trim()} {row.LastName.Trim()}",
        row.SpecialtyIds.Zip(row.SpecialtyNames, (id, name) => new DoctorPublicProfileDto.Reference(id, name)).ToArray(),
        new(row.ProvinceId, row.ProvinceName),
        new(row.MunicipalityId, row.MunicipalityName),
        row.AddressLine, row.ConsultationFee, row.YearsOfExperience, row.Biography);

    private sealed record PrivateProfileRow(int Id, string PhoneNumber, string LicenseNumber);
}
