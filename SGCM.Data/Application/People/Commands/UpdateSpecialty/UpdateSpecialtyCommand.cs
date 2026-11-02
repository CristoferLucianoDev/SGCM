using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Commands.UpdateSpecialty;

public sealed record UpdateSpecialtyCommand(
    int Id,
    bool NameProvided,
    string? Name,
    bool DescriptionProvided,
    string? Description) : ICommand<Unit>;
