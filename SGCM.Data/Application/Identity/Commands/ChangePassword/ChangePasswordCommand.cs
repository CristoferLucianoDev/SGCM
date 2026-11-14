using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.ChangePassword;

/// <summary>
/// Cambia la contraseña de la identidad autenticada. Conserva la sesión actual y revoca las demás.
/// </summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Unit>
{
    public override string ToString() =>
        $"{nameof(ChangePasswordCommand)} {{ CurrentPassword = ********, NewPassword = ******** }}";
}
