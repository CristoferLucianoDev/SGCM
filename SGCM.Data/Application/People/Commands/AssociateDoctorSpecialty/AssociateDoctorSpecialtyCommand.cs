using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;

public sealed record AssociateDoctorSpecialtyCommand(int SpecialtyId) : ICommand<Unit>;
