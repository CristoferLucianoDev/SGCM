using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.People.Queries.SearchDoctors;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Perfil público de un médico: los campos del directorio <c>people.vw_doctor_directory</c>, sin teléfono ni licencia.
/// La usan el perfil del médico y el detalle de la cita del paciente, para que el paciente vea siempre los mismos
/// datos. La vista solo incluye médicos activos, así que un médico desactivado no tiene perfil público.
/// </summary>
internal static class DoctorPublicProfileProjection
{
    /// <summary>Consulta componible de la fila del directorio del médico; vacía si no está activo.</summary>
    public static IQueryable<DoctorSearchDto> Query(AppDbContext context, int doctorId) =>
        context.Database.SqlQuery<DoctorSearchDto>($"""
            SELECT id, first_name, last_name, province_id, province_name, municipality_id, municipality_name,
                   address_line, consultation_fee, years_of_experience, biography, specialty_ids, specialty_names
            FROM people.vw_doctor_directory
            WHERE id = {doctorId}
            """);

    /// <summary>Perfil público del médico, o <c>null</c> si no existe o está desactivado.</summary>
    public static async Task<DoctorPublicProfileDto?> GetAsync(
        AppDbContext context, int doctorId, CancellationToken cancellationToken)
    {
        var row = await Query(context, doctorId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    /// <summary>Convierte la fila del directorio en el perfil público.</summary>
    public static DoctorPublicProfileDto Map(DoctorSearchDto row) => new(
        row.Id,
        $"{row.FirstName.Trim()} {row.LastName.Trim()}",
        row.SpecialtyIds.Zip(row.SpecialtyNames, (id, name) => new DoctorPublicProfileDto.Reference(id, name)).ToArray(),
        new(row.ProvinceId, row.ProvinceName),
        new(row.MunicipalityId, row.MunicipalityName),
        row.AddressLine, row.ConsultationFee, row.YearsOfExperience, row.Biography);
}
