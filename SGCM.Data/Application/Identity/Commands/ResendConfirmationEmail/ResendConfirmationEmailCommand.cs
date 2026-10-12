using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.ResendConfirmationEmail;

/// <summary>
/// Pide reenviar el correo de confirmación de un registro pendiente.
/// </summary>
/// <param name="Email">Correo tal como lo escribió el usuario.</param>
public sealed record ResendConfirmationEmailCommand(string Email) : ICommand<Unit>;
