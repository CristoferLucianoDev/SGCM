using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.GenerateAvailabilityBlocks;

/// <summary>
/// Cada día el horizonte avanza un día; el generador agrega solo los bloques que faltan. El decorador
/// guarda todos los bloques en una sola transacción.
/// </summary>
internal sealed class GenerateAvailabilityBlocksCommandHandler(
    IDoctorAvailabilitySeriesRepository seriesRepository,
    AvailabilityBlockGenerator generator,
    IDateTimeProvider clock) : ICommandHandler<GenerateAvailabilityBlocksCommand, int>
{
    public async Task<Result<int>> HandleAsync(GenerateAvailabilityBlocksCommand command, CancellationToken cancellationToken)
    {
        // Un día de margen: la fecha UTC puede ir un día por delante de la fecha local de la serie.
        var from = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-1);
        var activeSeries = await seriesRepository.ListActiveFromAsync(from, cancellationToken);

        var generated = 0;
        foreach (var series in activeSeries)
        {
            generated += await generator.GenerateAsync(series, cancellationToken);
        }

        return generated;
    }
}
