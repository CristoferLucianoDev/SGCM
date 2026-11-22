namespace SGCM.Web.Contracts.People;

/// <summary>
/// Estado de los proveedores de seguros que pide el listado. Solo el administrador puede ver los inactivos.
/// </summary>
public enum InsuranceProviderStatusFilter
{
    /// <summary>Solo los proveedores activos.</summary>
    Active = 0,

    /// <summary>Solo los proveedores inactivos.</summary>
    Inactive = 1,

    /// <summary>Los proveedores activos e inactivos.</summary>
    All = 2
}
