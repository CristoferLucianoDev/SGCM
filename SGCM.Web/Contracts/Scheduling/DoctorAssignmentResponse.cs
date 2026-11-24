namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Médico asignado y su tarifa, para el resumen antes de enviar la solicitud.</summary>
/// <param name="DoctorId">Id del médico asignado.</param>
/// <param name="DoctorName">Nombre completo del médico.</param>
/// <param name="ConsultationFee">Tarifa de consulta del médico.</param>
public sealed record DoctorAssignmentResponse(int DoctorId, string DoctorName, decimal ConsultationFee);
