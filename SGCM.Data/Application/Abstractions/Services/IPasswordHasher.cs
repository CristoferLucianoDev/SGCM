using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Services;

/// <summary>
/// Calcula el hash de las contraseñas. La contraseña en claro nunca se persiste.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Devuelve el hash de la contraseña, con su sal incluida.
    /// </summary>
    string Hash(Password password);

    /// <summary>
    /// Indica si la contraseña en claro corresponde al hash guardado. Recibe la contraseña
    /// como texto y no como <see cref="Password"/> porque una contraseña guardada antes de
    /// cambiar las reglas debe poder verificarse aunque ya no las cumpla.
    /// Si <paramref name="passwordHash"/> es <c>null</c>, verifica contra un hash de relleno y
    /// devuelve <c>false</c>: así, un correo inexistente tarda lo mismo que una contraseña
    /// incorrecta, y el tiempo de respuesta no revela qué cuentas existen.
    /// </summary>
    /// <param name="passwordHash">Hash guardado de la contraseña, o <c>null</c> si no hay usuario.</param>
    /// <param name="password">Contraseña en claro recibida.</param>
    /// <returns>
    /// <c>true</c> si la contraseña es correcta; <c>false</c> si no lo es, si no hay hash o si
    /// el hash está vacío o mal formado.
    /// </returns>
    bool Verify(string? passwordHash, string password);
}