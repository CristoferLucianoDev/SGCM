namespace SGCM.Data.Domain.Common;

/// <summary>
/// Clase base de todas las entidades del dominio: objetos con identidad propia
/// que persiste a lo largo de su ciclo de vida.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// Identificador de la entidad. Lo asigna la base de datos al persistirla,
    /// por lo que vale <c>0</c> mientras la entidad no se haya guardado.
    /// </summary>
    public int Id { get; protected init; }
}
