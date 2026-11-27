using System.Net;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;

namespace SGCM.Data.Domain.Clinical.MedicalRecords;

/// <summary>
/// Fila del registro de accesos: deja constancia de quién consultó, creó o corrigió una versión del registro clínico,
/// cuándo y desde qué dirección. Es de solo inserción: no tiene métodos que la modifiquen, y la base tampoco permite
/// actualizarla ni borrarla.
/// </summary>
public sealed class MedicalRecordAccessLog : AggregateRoot
{
    /// <summary>Longitud máxima de la dirección IP, igual que en la base: cabe una IPv6 completa.</summary>
    public const int IpAddressMaxLength = 45;

    private MedicalRecordAccessLog()
    {
    }

    /// <summary>Versión del registro clínico a la que se accedió.</summary>
    public int MedicalRecordId { get; private set; }

    /// <summary>Usuario que accedió.</summary>
    public int AccessedBy { get; private set; }

    /// <summary>Si se consultó, se creó o se corrigió.</summary>
    public MedicalRecordAccessType AccessType { get; private set; }

    /// <summary>Momento del acceso, en UTC.</summary>
    public DateTimeOffset AccessedAt { get; private set; }

    /// <summary>Dirección IP del cliente en su forma canónica, o <c>null</c> si no se conoce o no es válida.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Registra un acceso a una versión del registro clínico.</summary>
    /// <param name="medicalRecordId">Versión a la que se accedió; ya debe estar guardada.</param>
    /// <param name="accessedBy">Usuario que accedió.</param>
    /// <param name="accessType">Tipo de acceso.</param>
    /// <param name="accessedAt">Momento del acceso.</param>
    /// <param name="ipAddress">
    /// Dirección IP del cliente. Si no es una IP o su forma canónica no cabe en la columna, se guarda <c>null</c>: se
    /// pierde el dato, pero el acceso queda registrado.
    /// </param>
    /// <exception cref="DomainException">Si la versión, el usuario o el tipo de acceso no son válidos.</exception>
    public static MedicalRecordAccessLog Record(
        int medicalRecordId,
        int accessedBy,
        MedicalRecordAccessType accessType,
        DateTimeOffset accessedAt,
        string? ipAddress)
    {
        // Los datos llegan del caso de uso, no del usuario: un valor inválido es un error de programación.
        if (medicalRecordId <= 0) throw new DomainException("La versión del registro clínico accedida no es válida.");
        if (accessedBy <= 0) throw new DomainException("El usuario que accede al registro clínico no es válido.");
        if (!Enum.IsDefined(accessType)) throw new DomainException("El tipo de acceso al registro clínico no es válido.");

        return new MedicalRecordAccessLog
        {
            MedicalRecordId = medicalRecordId,
            AccessedBy = accessedBy,
            AccessType = accessType,
            AccessedAt = accessedAt.ToUniversalTime(),
            IpAddress = NormalizeIpAddress(ipAddress)
        };
    }

    private static string? NormalizeIpAddress(string? ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var parsed)) return null;

        var canonical = parsed.ToString();
        return canonical.Length <= IpAddressMaxLength ? canonical : null;
    }
}
