namespace SGCM.Data.Application.Abstractions.Services;

/// <summary>
/// Fuente de azar inyectable, para que las pruebas de lo que se elige al azar sean deterministas.
/// </summary>
public interface IRandomNumberGenerator
{
    /// <summary>Devuelve un entero al azar en [0, <paramref name="maxExclusive"/>).</summary>
    /// <param name="maxExclusive">Límite superior excluido; debe ser positivo.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si el límite no es positivo.</exception>
    int Next(int maxExclusive);
}
