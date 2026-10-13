using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Identity.Commands.Login;

/// <summary>
/// Inicio de sesión con correo y contraseña.
/// </summary>
/// <param name="Email">Correo tal como lo escribió el usuario.</param>
/// <param name="Password">Contraseña en claro.</param>
/// <param name="DeviceName">
/// Nombre del dispositivo. Lo llena la API a partir de la petición, nunca del cuerpo.
/// </param>
/// <param name="IpAddress">
/// Dirección IP del cliente. La llena la API a partir de la petición, nunca del cuerpo.
/// </param>
public sealed record LoginCommand(string Email, string Password, string? DeviceName, string? IpAddress)
    : ICommand<LoginResult>
{
    /// <summary>
    /// Oculta la contraseña, para que no se filtre si el comando termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(LoginCommand)} {{ Email = {Email}, Password = ********, DeviceName = {DeviceName}, IpAddress = {IpAddress} }}";
}
