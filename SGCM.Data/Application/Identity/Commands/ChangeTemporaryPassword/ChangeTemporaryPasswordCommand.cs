using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;

namespace SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;

/// <summary>Cambia la contraseña temporal de la identidad autenticada y renueva sus credenciales.</summary>
public sealed record ChangeTemporaryPasswordCommand(string NewPassword) : ICommand<SessionTokens>
{
    public override string ToString() => $"{nameof(ChangeTemporaryPasswordCommand)} {{ NewPassword = ******** }}";
}

