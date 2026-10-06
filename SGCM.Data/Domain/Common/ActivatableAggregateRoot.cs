namespace SGCM.Data.Domain.Common;

/// <summary>
/// Raíz de agregado que admite baja lógica: en lugar de eliminarse, se desactiva
/// y puede reactivarse más adelante. Toda instancia nueva nace activa.
/// </summary>
public abstract class ActivatableAggregateRoot : AggregateRoot
{
    /// <summary>
    /// Indica si el registro está activo.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Nombre de la entidad usado como prefijo en los códigos de error
    /// (por ejemplo <c>User.AlreadyActive</c>). Se declara explícitamente en cada
    /// agregado para que no dependa del tipo en tiempo de ejecución, que con los
    /// proxies de EF Core sería el nombre del proxy y no el de la entidad.
    /// </summary>
    protected abstract string EntityName { get; }

    /// <summary>
    /// Punto de extensión que se ejecuta después de activar el registro.
    /// </summary>
    protected virtual void OnActivated() { }

    /// <summary>
    /// Punto de extensión que se ejecuta después de desactivar el registro.
    /// </summary>
    protected virtual void OnDeactivated() { }

    /// <summary>
    /// Activa el registro.
    /// </summary>
    /// <returns>Éxito, o un error de conflicto si el registro ya estaba activo.</returns>
    public Result Activate()
    {
        if (IsActive)
        {
            return ActivationErrors.AlreadyActive(EntityName);
        }

        IsActive = true;
        OnActivated();
        return Result.Success();
    }

    /// <summary>
    /// Desactiva el registro (baja lógica).
    /// </summary>
    /// <returns>Éxito, o un error de conflicto si el registro ya estaba inactivo.</returns>
    public Result Deactivate()
    {
        if (!IsActive)
        {
            return ActivationErrors.AlreadyInactive(EntityName);
        }

        IsActive = false;
        OnDeactivated();
        return Result.Success();
    }
}
