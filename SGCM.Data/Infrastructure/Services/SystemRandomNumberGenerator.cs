using SGCM.Data.Application.Abstractions.Services;

namespace SGCM.Data.Infrastructure.Services;

internal sealed class SystemRandomNumberGenerator : IRandomNumberGenerator
{
    public int Next(int maxExclusive)
    {
        // Random.Next(0) devuelve 0 sin error; aquí un límite no positivo es un error de programación.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return Random.Shared.Next(maxExclusive);
    }
}
