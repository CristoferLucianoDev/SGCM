using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class DoctorSearchReader(AppDbContext context) : IDoctorSearchReader
{
    public async Task<PagedResult<DoctorSearchDto>> SearchAsync(
        string? search, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Database.SqlQuery<DoctorSearchDto>($"""
            SELECT id, first_name, last_name, province_id, province_name, municipality_id, municipality_name,
                   address_line, consultation_fee, years_of_experience, biography, specialty_ids, specialty_names
            FROM people.vw_doctor_directory
            """);

        if (search is not null)
        {
            query = query.Where(doctor => PostgresFunctions.NormalizeText(doctor.FirstName.Trim() + " " + doctor.LastName.Trim())!
                .Contains(PostgresFunctions.NormalizeText(search)!));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = checked((int)(((long)pageNumber - 1) * pageSize));
        var items = await query.OrderBy(doctor => doctor.FirstName).ThenBy(doctor => doctor.LastName)
            .ThenBy(doctor => doctor.Id).Skip(offset).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<DoctorSearchDto>(items, pageNumber, pageSize, totalCount);
    }
}
