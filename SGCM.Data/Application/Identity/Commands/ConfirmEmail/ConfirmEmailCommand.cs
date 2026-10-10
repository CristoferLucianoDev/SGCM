using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.ConfirmEmail;

/// <summary>
/// Confirmación del correo de una cuenta pendiente con el token en claro recibido en el enlace.
/// </summary>
/// <param name="Token">Token en claro recibido en el enlace de confirmación.</param>
public sealed record ConfirmEmailCommand(string Token) : ICommand<Unit>
{
    /// <summary>
    /// Oculta el token, para que no se filtre si el comando termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(ConfirmEmailCommand)} {{ Token = ******** }}";
}
