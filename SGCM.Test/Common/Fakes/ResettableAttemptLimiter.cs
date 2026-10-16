using Serilog;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Infrastructure.Security;

namespace SGCM.Test.Common.Fakes;

/// <summary>
/// <see cref="IAttemptLimiter"/> de la app de pruebas: delega en un <see cref="MemoryAttemptLimiter"/>
/// y lo reemplaza por uno vacío en <see cref="Clear"/>, para que los intentos de una prueba no cuenten
/// en la siguiente sin tocar el código de producción.
/// </summary>
/// <param name="dateTimeProvider">Reloj de la app de pruebas.</param>
/// <param name="logger">Logger del contenedor de la app.</param>
internal sealed class ResettableAttemptLimiter(IDateTimeProvider dateTimeProvider, ILogger logger) : IAttemptLimiter
{
    private volatile MemoryAttemptLimiter _inner = new(dateTimeProvider, logger);

    /// <inheritdoc/>
    public bool IsLimitExceeded(string key, int maxAttempts) => _inner.IsLimitExceeded(key, maxAttempts);

    /// <inheritdoc/>
    public void RegisterAttempt(string key, TimeSpan window) => _inner.RegisterAttempt(key, window);

    /// <inheritdoc/>
    public bool TryRegisterAttempt(string key, int maxAttempts, TimeSpan window) =>
        _inner.TryRegisterAttempt(key, maxAttempts, window);

    /// <inheritdoc/>
    public void Reset(string key) => _inner.Reset(key);

    /// <summary>
    /// Descarta todos los intentos registrados.
    /// </summary>
    public void Clear() => _inner = new MemoryAttemptLimiter(dateTimeProvider, logger);
}