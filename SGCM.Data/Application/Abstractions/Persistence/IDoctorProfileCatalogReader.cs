namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>Consulta el catálogo del alta médica y conserva los bloqueos hasta el final de su transacción.</summary>
internal interface IDoctorProfileCatalogReader
{
    Task<DoctorProfileLocation> GetLocationAsync(int provinceId, int municipalityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DoctorProfileSpecialty>> GetSpecialtiesAsync(int[] specialtyIds, CancellationToken cancellationToken);
}

internal sealed record DoctorProfileLocation(bool ProvinceExists, int? MunicipalityProvinceId);

internal sealed record DoctorProfileSpecialty(int Id, bool IsActive);
