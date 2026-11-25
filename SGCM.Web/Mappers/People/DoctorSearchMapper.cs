using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

public static class DoctorSearchMapper
{
    public static SearchDoctorsQuery ToQuery(this SearchDoctorsRequest request) => new(
        Search: request.Search,
        PageNumber: request.PageNumber,
        PageSize: request.PageSize,
        SpecialtyId: request.SpecialtyId,
        ProvinceId: request.ProvinceId,
        MunicipalityId: request.MunicipalityId);

    public static DoctorSearchResponse ToResponse(this DoctorSearchDto doctor)
    {
        if (doctor.SpecialtyIds.Length != doctor.SpecialtyNames.Length)
        {
            throw new InvalidOperationException("Las referencias de especialidades del médico son inconsistentes.");
        }

        return new DoctorSearchResponse(
            Id: doctor.Id,
            FullName: $"{doctor.FirstName.Trim()} {doctor.LastName.Trim()}",
            Specialties: doctor.SpecialtyIds.Zip(doctor.SpecialtyNames,
                (id, name) => new DoctorPublicProfileResponse.Reference(id, name)).ToArray(),
            Province: new ProvinceResponse(doctor.ProvinceId, doctor.ProvinceName),
            Municipality: new MunicipalityResponse(doctor.MunicipalityId, doctor.MunicipalityName),
            ConsultationFee: doctor.ConsultationFee);
    }

    public static SearchDoctorsResponse ToResponse(this PagedResult<DoctorSearchDto> page) => new(
        Items: page.Items.Select(doctor => doctor.ToResponse()).ToArray(),
        PageNumber: page.PageNumber,
        PageSize: page.PageSize,
        TotalCount: page.TotalCount);
}
