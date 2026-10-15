using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.RequestPasswordReset;

/// <summary>
/// Pide el enlace para restablecer la contraseña de una cuenta.
/// </summary>
/// <param name="Email">Correo tal como lo escribió el usuario.</param>
public sealed record RequestPasswordResetCommand(string Email) : ICommand<Unit>;
