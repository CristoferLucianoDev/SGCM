using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IUserProfileReader"/> con consultas directas a las tablas
/// de perfil del esquema <c>people</c>.
/// </summary>
internal sealed class UserProfileReader(AppDbContext context) : IUserProfileReader
{
    // Consulta directa porque las entidades de perfil aún no existen; cuando existan, puede
    // pasar a sus repositorios. Cada rama tiene su propia consulta para que solo userId vaya
    // como parámetro y el nombre de la tabla nunca se interpole.
    public Task<bool> HasProfileAsync(int userId, UserRole role, CancellationToken cancellationToken)
    {
        var query = role switch
        {
            UserRole.Patient => context.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM people.patient WHERE user_id = {userId}) AS \"Value\""),
            UserRole.Doctor => context.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM people.doctor WHERE user_id = {userId}) AS \"Value\""),
            UserRole.Administrator => context.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM people.administrator WHERE user_id = {userId}) AS \"Value\""),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "El rol no tiene una tabla de perfil.")
        };

        return query.SingleAsync(cancellationToken);
    }
}
