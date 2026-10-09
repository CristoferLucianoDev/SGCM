namespace SGCM.Data.Domain.Common;

/// <summary>
/// Representa "sin valor" para los casos de uso que no devuelven datos.
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = new();
}