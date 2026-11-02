using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Commands.ActivateSpecialty;

public sealed record ActivateSpecialtyCommand(int Id) : ICommand<Unit>;
