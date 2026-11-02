using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;

public sealed record DisassociateDoctorSpecialtyCommand(int SpecialtyId) : ICommand<Unit>;
