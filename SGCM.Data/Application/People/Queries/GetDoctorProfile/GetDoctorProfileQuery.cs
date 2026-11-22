using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.People.Queries.GetDoctorProfile;

public sealed record GetDoctorProfileQuery(int DoctorId) : IQuery<DoctorProfileResult>;
