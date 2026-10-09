namespace SGCM.Data.Domain.Common.Interfaces;

public interface IRepository<in TAggregate> where TAggregate : AggregateRoot
{
    void Add(TAggregate aggregate);
}
