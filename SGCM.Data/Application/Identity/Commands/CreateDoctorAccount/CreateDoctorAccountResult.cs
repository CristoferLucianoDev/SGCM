namespace SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;

/// <summary>
/// Cuenta de médico creada y su contraseña temporal.
/// </summary>
/// <param name="UserId">Id del usuario creado.</param>
/// <param name="Email">Correo normalizado de la cuenta.</param>
/// <param name="TemporaryPassword">
/// Contraseña temporal en claro. Es la única vez que se conoce: solo se guarda su hash.
/// </param>
public sealed record CreateDoctorAccountResult(int UserId, string Email, string TemporaryPassword)
{
    /// <summary>
    /// Oculta la contraseña, para que no se filtre si el resultado termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(CreateDoctorAccountResult)} {{ UserId = {UserId}, Email = {Email}, TemporaryPassword = ******** }}";
}