namespace SGCM.Data.Application.Abstractions.Messaging;

/// <summary>
/// Comando que accede al registro clínico. Se envuelve en el decorador del registro de accesos, que guarda una fila
/// por cada versión que el handler informe con <c>MedicalRecordAccessTracker</c>, con el usuario autenticado, la hora
/// y la dirección IP del cliente.
/// </summary>
public interface IAccessLoggedCommand<TResponse> : ICommand<TResponse>
{
    /// <summary>Dirección IP del cliente. La llena la API a partir de la petición.</summary>
    string? IpAddress { get; }
}
