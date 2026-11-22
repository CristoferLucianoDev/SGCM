namespace SGCM.Data.Application.People.Queries.GetDoctorProfile;

public sealed record DoctorPublicProfileDto(
    int Id,
    string FullName,
    IReadOnlyList<DoctorPublicProfileDto.Reference> Specialties,
    DoctorPublicProfileDto.Reference Province,
    DoctorPublicProfileDto.Reference Municipality,
    string AddressLine,
    decimal ConsultationFee,
    int YearsOfExperience,
    string Biography) : DoctorProfileResult
{
    public sealed record Reference(int Id, string Name);
}
