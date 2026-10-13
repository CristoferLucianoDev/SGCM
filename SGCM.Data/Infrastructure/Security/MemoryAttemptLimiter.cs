using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using Serilog;

namespace SGCM.Data.Infrastructure.Security;

/// <summary>
/// Implementación en memoria de <see cref="IAttemptLimiter"/>. Usa un diccionario propio
/// porque <c>MemoryCache</c> borra entradas vigentes al compactar o al escribir con el caché
/// lleno, y eso libera el límite justo durante un ataque. Con el diccionario lleno de ventanas
/// vigentes, falla abierto para las claves nuevas a propósito: fallar cerrado dejaría a
/// cualquiera bloquear el inicio de sesión de todos llenándolo con claves al azar, y esas
/// claves nuevas quedan cubiertas por el límite por IP.
/// </summary>
internal sealed class MemoryAttemptLimiter : IAttemptLimiter
{
    private const int MaxTrackedKeys = 100_000;

    private static readonly TimeSpan FullWarningInterval = TimeSpan.FromMinutes(1);

    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger _logger;
    private readonly int _capacity;
    private readonly Dictionary<string, AttemptWindow> _windows = [];

    // El volumen es bajo y así no se pierden incrementos simultáneos.
    private readonly object _sync = new();

    private DateTimeOffset? _lastFullWarningAt;

    // Fin más próximo entre las ventanas guardadas; antes de ese momento no hay nada vencido que borrar.
    private DateTimeOffset _earliestEndsAt = DateTimeOffset.MaxValue;

    public MemoryAttemptLimiter(IDateTimeProvider dateTimeProvider, ILogger logger)
        : this(dateTimeProvider, logger, MaxTrackedKeys)
    {
    }

    internal MemoryAttemptLimiter(IDateTimeProvider dateTimeProvider, ILogger logger, int capacity)
    {
        _dateTimeProvider = dateTimeProvider;
        _logger = logger.ForContext<MemoryAttemptLimiter>();
        _capacity = capacity;
    }

    public bool IsLimitExceeded(string key, int maxAttempts)
    {
        lock (_sync)
        {
            var window = ActiveWindow(key, _dateTimeProvider.UtcNow);
            return window is not null && window.Attempts >= maxAttempts;
        }
    }

    public void RegisterAttempt(string key, TimeSpan window)
    {
        lock (_sync)
        {
            Register(key, window, _dateTimeProvider.UtcNow);
        }
    }

    public bool TryRegisterAttempt(string key, int maxAttempts, TimeSpan window)
    {
        lock (_sync)
        {
            var now = _dateTimeProvider.UtcNow;

            if (ActiveWindow(key, now) is { } active && active.Attempts >= maxAttempts)
            {
                return false;
            }

            Register(key, window, now);
            return true;
        }
    }

    public void Reset(string key)
    {
        lock (_sync)
        {
            _windows.Remove(key);
        }
    }

    private AttemptWindow? ActiveWindow(string key, DateTimeOffset now) =>
        _windows.TryGetValue(key, out var window) && now < window.EndsAt ? window : null;

    // Los intentos se suman sobre la ventana guardada: una ventana vigente nunca se reemplaza.
    private void Register(string key, TimeSpan window, DateTimeOffset now)
    {
        if (ActiveWindow(key, now) is { } active)
        {
            active.Attempts++;
            return;
        }

        if (!_windows.ContainsKey(key) && !HasRoomForNewKey(now))
        {
            WarnFull(now);
            return;
        }

        var endsAt = now + window;
        _windows[key] = new AttemptWindow(endsAt);

        if (endsAt < _earliestEndsAt)
        {
            _earliestEndsAt = endsAt;
        }
    }

    // Con el diccionario lleno de ventanas vigentes, recorrerlo en cada clave nueva haría
    // esperar a todos los inicios de sesión detrás del lock; solo vale la pena volver a
    // limpiar cuando ya venció la ventana que termina primero.
    private bool HasRoomForNewKey(DateTimeOffset now)
    {
        if (_windows.Count < _capacity)
        {
            return true;
        }

        if (now < _earliestEndsAt)
        {
            return false;
        }

        RemoveExpiredWindows(now);
        return _windows.Count < _capacity;
    }

    // Desde .NET Core 3.0, Dictionary admite Remove mientras se recorre, así que no hace
    // falta una lista intermedia.
    private void RemoveExpiredWindows(DateTimeOffset now)
    {
        _earliestEndsAt = DateTimeOffset.MaxValue;

        foreach (var (key, window) in _windows)
        {
            if (window.EndsAt <= now)
            {
                _windows.Remove(key);
            }
            else if (window.EndsAt < _earliestEndsAt)
            {
                _earliestEndsAt = window.EndsAt;
            }
        }
    }

    // Como máximo un aviso por minuto para no inundar los logs durante un ataque. La clave no
    // se registra porque lleva el correo del usuario.
    private void WarnFull(DateTimeOffset now)
    {
        if (_lastFullWarningAt is { } last && now < last + FullWarningInterval)
        {
            return;
        }

        _lastFullWarningAt = now;
        _logger.Warning(
            "El limitador de intentos está lleno con {Capacity} ventanas vigentes; los intentos de claves nuevas no se cuentan.",
            _capacity);
    }

    // Clase mutable para sumar intentos sin volver a escribir la entrada.
    private sealed class AttemptWindow(DateTimeOffset endsAt)
    {
        public int Attempts { get; set; } = 1;

        public DateTimeOffset EndsAt { get; } = endsAt;
    }
}
