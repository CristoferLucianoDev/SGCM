using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class DoctorSearchReader(AppDbContext context) : IDoctorSearchReader
{
    public async Task<PagedResult<DoctorSearchDto>> SearchAsync(
        DoctorSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var query = context.Database.SqlQuery<DoctorSearchDto>($"""
            SELECT id, first_name, last_name, province_id, province_name, municipality_id, municipality_name,
                   address_line, consultation_fee, years_of_experience, biography, specialty_ids, specialty_names
            FROM people.vw_doctor_directory
            """).Where(doctor => doctor.SpecialtyIds.Length > 0);

        if (criteria.Search is { } search)
        {
            query = query.Where(doctor => PostgresFunctions.NormalizeText(doctor.FirstName.Trim() + " " + doctor.LastName.Trim())!
                .Contains(PostgresFunctions.NormalizeText(search)!));
        }

        if (criteria.SpecialtyId is { } specialtyId)
        {
            query = query.Where(doctor => doctor.SpecialtyIds.Contains(specialtyId));
        }

        if (criteria.ProvinceId is { } provinceId)
        {
            query = query.Where(doctor => doctor.ProvinceId == provinceId);
        }

        if (criteria.MunicipalityId is { } municipalityId)
        {
            query = query.Where(doctor => doctor.MunicipalityId == municipalityId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = checked((int)(((long)criteria.PageNumber - 1) * criteria.PageSize));
        var items = await query.OrderBy(doctor => doctor.FirstName).ThenBy(doctor => doctor.LastName)
            .ThenBy(doctor => doctor.Id).Skip(offset).Take(criteria.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<DoctorSearchDto>(items, criteria.PageNumber, criteria.PageSize, totalCount);
    }
}
