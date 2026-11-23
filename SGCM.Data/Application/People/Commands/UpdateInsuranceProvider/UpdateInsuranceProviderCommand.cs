using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Application.People.Commands.UpdateInsuranceProvider;

/// <summary>
/// Actualización parcial de un proveedor de seguros por un administrador: los campos no enviados
/// conservan su valor.
/// </summary>
/// <param name="Id">Id del proveedor que se actualiza.</param>
/// <param name="Changes">Campos enviados; un sitio web enviado nulo o vacío lo quita.</param>
public sealed record UpdateInsuranceProviderCommand(int Id, InsuranceProviderChanges Changes) : ICommand<Unit>;
