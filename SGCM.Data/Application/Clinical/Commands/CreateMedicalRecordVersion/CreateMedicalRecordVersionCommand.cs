using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;

/// <summary>
/// El médico autenticado registra la información clínica de una cita propia: la primera vez crea el registro, y
/// después cada envío crea una versión nueva que corrige la anterior sin modificarla.
/// </summary>
/// <param name="AppointmentId">Cita que se registra.</param>
/// <param name="Diagnosis">Diagnóstico, hasta 1000 caracteres; opcional.</param>
/// <param name="Treatment">Tratamiento, hasta 1000 caracteres; opcional.</param>
/// <param name="Notes">Notas, hasta 2000 caracteres; opcional. Al menos una de las tres partes debe tener texto.</param>
/// <param name="IpAddress">Dirección IP del cliente. La llena la API a partir de la petición.</param>
public sealed record CreateMedicalRecordVersionCommand(
    int AppointmentId,
    string? Diagnosis,
    string? Treatment,
    string? Notes,
    string? IpAddress) : IAccessLoggedCommand<MedicalRecordVersionResult>;
