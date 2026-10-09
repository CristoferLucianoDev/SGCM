using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;

/// <summary>
/// Registro público de un paciente. No lleva rol: toda cuenta creada aquí es Patient.
/// </summary>
/// <param name="Email">Correo tal como lo escribió el usuario.</param>
/// <param name="Password">Contraseña en claro.</param>
public sealed record RegisterPatientAccountCommand(string Email, string Password) : ICommand<Unit>
{
    /// <summary>
    /// Oculta la contraseña, para que no se filtre si el comando termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(RegisterPatientAccountCommand)} {{ Email = {Email}, Password = ******** }}";
}