using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;

/// <summary>
/// Consulta el historial médico de un paciente: el propio, si lo pide un paciente, o el de un paciente con quien el
/// médico autenticado tiene al menos una cita. Cada versión mostrada queda en el registro de accesos.
/// </summary>
/// <param name="PatientId">
/// Paciente cuyo historial consulta un médico; <c>null</c> cuando el paciente autenticado consulta el suyo.
/// </param>
/// <param name="IpAddress">Dirección IP del cliente. La llena la API a partir de la petición.</param>
public sealed record GetMedicalHistoryQuery(int? PatientId, string? IpAddress) : IQuery<MedicalHistoryDto>;
