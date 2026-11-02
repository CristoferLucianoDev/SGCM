using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Commands.DeactivateSpecialty;

public sealed record DeactivateSpecialtyCommand(int Id) : ICommand<Unit>;
