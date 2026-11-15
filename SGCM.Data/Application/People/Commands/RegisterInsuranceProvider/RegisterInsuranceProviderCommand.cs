using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;

/// <summary>
/// Registro de un proveedor de seguros por un administrador. Devuelve el id del proveedor creado.
/// </summary>
public sealed record RegisterInsuranceProviderCommand(
    string Name,
    string ContactPhone,
    string ContactEmail,
    string Address,
    string? Website) : ICommand<int>;
