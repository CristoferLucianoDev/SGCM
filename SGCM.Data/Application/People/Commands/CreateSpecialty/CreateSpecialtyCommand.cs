using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.People.Commands.CreateSpecialty;

public sealed record CreateSpecialtyCommand(string Name, string Description) : ICommand<int>;
