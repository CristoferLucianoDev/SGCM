namespace SGCM.Data.Domain.Common;

/// <summary>
/// Errores de las operaciones de activación y desactivación de
/// <see cref="ActivatableAggregateRoot"/>.
/// </summary>
public static class ActivationErrors
{
    /// <summary>
    /// Se intentó desactivar un registro que ya estaba inactivo.
    /// </summary>
    /// <param name="entityName">Nombre de la entidad, usado como prefijo del código.</param>
    public static Error AlreadyInactive(string entityName) =>
        Error.Conflict($"{entityName}.AlreadyInactive", "El registro ya está inactivo.");

    /// <summary>
    /// Se intentó activar un registro que ya estaba activo.
    /// </summary>
    /// <param name="entityName">Nombre de la entidad, usado como prefijo del código.</param>
    public static Error AlreadyActive(string entityName) =>
        Error.Conflict($"{entityName}.AlreadyActive", "El registro ya está activo.");
}
