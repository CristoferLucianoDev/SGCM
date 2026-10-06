namespace SGCM.Data.Domain.Common.Exceptions;

/// <summary>
/// Excepción para violaciones de invariantes que indican un error de programación
/// (por ejemplo, invocar el dominio sin un dato obligatorio). Los fallos de negocio
/// esperados no usan esta excepción: se devuelven como <see cref="Result"/>.
/// </summary>
public sealed class DomainException : Exception
{
    /// <summary>
    /// Crea la excepción con un mensaje que describe la invariante violada.
    /// </summary>
    public DomainException(string message) : base(message)
    {
    }
}
