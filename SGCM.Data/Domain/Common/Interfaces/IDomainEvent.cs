namespace SGCM.Data.Domain.Common.Interfaces;

/// <summary>
/// Marca un hecho relevante que ocurrió en el dominio. Los agregados los registran
/// con <see cref="AggregateRoot"/> y la infraestructura los despacha después de persistir.
/// </summary>
public interface IDomainEvent
{
}
