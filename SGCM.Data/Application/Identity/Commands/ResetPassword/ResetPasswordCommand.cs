using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.ResetPassword;

/// <summary>
/// Restablecimiento de la contraseña con el token en claro recibido en el enlace de recuperación.
/// </summary>
/// <param name="Token">Token en claro recibido en el enlace de recuperación.</param>
/// <param name="NewPassword">Contraseña nueva en claro.</param>
public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand<Unit>
{
    /// <summary>
    /// Oculta el token y la contraseña, para que no se filtren si el comando termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(ResetPasswordCommand)} {{ Token = ********, NewPassword = ******** }}";
}
