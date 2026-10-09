using SGCM.Data.Application.Abstractions.Services;

namespace SGCM.Data.Infrastructure.Services;

internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}