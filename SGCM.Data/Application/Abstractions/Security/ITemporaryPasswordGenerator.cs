namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Genera contraseñas temporales para las cuentas que se crean con cambio obligatorio
/// de contraseña. Cada contraseña es aleatoria y cumple la política de <c>Password</c>.
/// </summary>
public interface ITemporaryPasswordGenerator
{
    /// <summary>
    /// Genera una contraseña temporal nueva.
    /// </summary>
    /// <returns>La contraseña en claro, que solo debe mostrarse una vez.</returns>
    string Generate();
}