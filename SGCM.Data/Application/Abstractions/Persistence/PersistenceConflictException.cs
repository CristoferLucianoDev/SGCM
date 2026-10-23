using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Abstractions.Persistence;

// Transporta un conflicto reconocido hasta el límite transaccional, sin exponer el proveedor.
internal sealed class PersistenceConflictException(Error error, Exception innerException)
    : Exception(error.Description, innerException)
{
    public Error Error { get; } = error;
}
