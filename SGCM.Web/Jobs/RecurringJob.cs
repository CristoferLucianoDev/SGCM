using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using ILogger = Serilog.ILogger;

namespace SGCM.Web.Jobs;

/// <summary>
/// Proceso automático que ejecuta <typeparamref name="TCommand"/> al arrancar y luego
/// cada intervalo configurado, y registra cuántos registros procesó cada ejecución.
/// </summary>
/// <typeparam name="TCommand">Comando sin datos que devuelve la cantidad de registros procesados.</typeparam>
public abstract class RecurringJob<TCommand> : BackgroundService
    where TCommand : ICommand<int>, new()
{
    // El job vive como singleton y los handlers son scoped: cada ejecución abre su
    // propio scope. Es infraestructura de hosting, no un service locator en el negocio.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<WorkerOptions> _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Crea el proceso con su fábrica de scopes, su configuración y su logger.
    /// </summary>
    /// <param name="scopeFactory">Fábrica para abrir un scope por ejecución.</param>
    /// <param name="options">Configuración de los procesos automáticos.</param>
    /// <param name="logger">Logger de Serilog; se le agrega el tipo del job como contexto.</param>
    protected RecurringJob(IServiceScopeFactory scopeFactory, IOptions<WorkerOptions> options, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger.ForContext(GetType());
    }

    private string JobName => GetType().Name;

    /// <summary>
    /// Devuelve el intervalo entre ejecuciones de este proceso.
    /// </summary>
    /// <param name="intervals">Intervalos configurados de todos los procesos.</param>
    /// <returns>El intervalo que corresponde a este proceso.</returns>
    protected abstract TimeSpan GetInterval(WorkerIntervalsOptions intervals);

    /// <summary>
    /// Ejecuta el comando al arrancar y en cada intervalo hasta que se detiene la aplicación.
    /// No hace nada si los procesos automáticos están deshabilitados.
    /// </summary>
    /// <param name="stoppingToken">Se cancela cuando la aplicación se detiene.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.Information("{Job} está deshabilitado", JobName);
            return;
        }

        using var timer = new PeriodicTimer(GetInterval(_options.Value.Intervals));

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Ejecuta el comando una vez en un scope nuevo y registra el resultado.
    /// Los fallos se registran y no se propagan, para que el proceso siga en la próxima ejecución.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación de la ejecución.</param>
    /// <exception cref="OperationCanceledException">Si se cancela <paramref name="cancellationToken"/>.</exception>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, int>>();

            var result = await handler.HandleAsync(new TCommand(), cancellationToken);

            if (result.IsFailure)
            {
                _logger.Warning("{Job} falló con el error {ErrorCode}", JobName, result.Error.Code);
                return;
            }

            LogProcessed(result.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "{Job} lanzó una excepción", JobName);
        }
    }

    // Sin registros procesados va en Debug para no llenar el log en cada intervalo.
    private void LogProcessed(int count)
    {
        if (count > 0)
        {
            _logger.Information("{Job} procesó {Count} registros", JobName, count);
            return;
        }

        _logger.Debug("{Job} procesó {Count} registros", JobName, count);
    }
}
