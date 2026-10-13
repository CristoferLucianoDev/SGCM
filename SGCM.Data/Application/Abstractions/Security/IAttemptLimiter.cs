namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Cuenta intentos por clave en una ventana fija que empieza con el primer intento.
/// Quien llama arma la clave con un prefijo propio (por ejemplo, <c>login:</c> más el
/// correo normalizado), para que los distintos usos no se mezclen. Los contadores viven en
/// memoria: con varias instancias de la API, cada una cuenta por separado.
/// </summary>
public interface IAttemptLimiter
{
    /// <summary>
    /// Indica si la clave ya alcanzó el máximo de intentos en su ventana vigente.
    /// </summary>
    /// <param name="key">Clave con el prefijo del uso.</param>
    /// <param name="maxAttempts">Máximo de intentos permitidos en la ventana.</param>
    /// <returns><c>true</c> si en la ventana vigente ya hay <paramref name="maxAttempts"/> intentos o más.</returns>
    bool IsLimitExceeded(string key, int maxAttempts);

    /// <summary>
    /// Registra un intento. Si la clave no tiene una ventana vigente, abre una nueva de
    /// duración <paramref name="window"/> que empieza con este intento.
    /// </summary>
    /// <param name="key">Clave con el prefijo del uso.</param>
    /// <param name="window">Duración de la ventana que se abre con el primer intento.</param>
    void RegisterAttempt(string key, TimeSpan window);

    /// <summary>
    /// Comprueba el límite y registra el intento en un solo paso atómico. Es lo que debe usar
    /// quien limita una operación costosa (por ejemplo, verificar una contraseña): se llama
    /// antes de la operación y, si esta sale bien, se llama a <see cref="Reset"/>. Con una
    /// consulta y un registro separados, las peticiones simultáneas pasarían todas la consulta
    /// antes de que se registre la primera.
    /// </summary>
    /// <param name="key">Clave con el prefijo del uso.</param>
    /// <param name="maxAttempts">Máximo de intentos permitidos en la ventana.</param>
    /// <param name="window">Duración de la ventana que se abre con el primer intento.</param>
    /// <returns>
    /// <c>true</c> si el intento quedó registrado; <c>false</c> si la ventana vigente ya tenía
    /// <paramref name="maxAttempts"/> intentos o más, en cuyo caso no se registra.
    /// </returns>
    bool TryRegisterAttempt(string key, int maxAttempts, TimeSpan window);

    /// <summary>
    /// Borra el contador de la clave.
    /// </summary>
    /// <param name="key">Clave con el prefijo del uso.</param>
    void Reset(string key);
}
