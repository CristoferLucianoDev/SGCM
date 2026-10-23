using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.People.Specialties;

public interface ISpecialtyRepository : IRepository<Specialty>
{
    Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);
}
