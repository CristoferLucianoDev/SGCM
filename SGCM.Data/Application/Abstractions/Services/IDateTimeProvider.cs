namespace SGCM.Data.Application.Abstractions.Services;

/// <summary>
/// Fuente de la hora actual; permite controlar el tiempo en las pruebas.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>
    /// Fecha y hora actual en UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}