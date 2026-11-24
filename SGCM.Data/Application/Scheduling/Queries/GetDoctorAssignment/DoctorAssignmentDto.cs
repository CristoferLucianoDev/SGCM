namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;

/// <summary>Médico asignado y su tarifa, para mostrarlos antes de enviar la solicitud.</summary>
/// <param name="DoctorId">Id del médico asignado.</param>
/// <param name="DoctorName">Nombre completo del médico.</param>
/// <param name="ConsultationFee">Tarifa de consulta del médico.</param>
public sealed record DoctorAssignmentDto(int DoctorId, string DoctorName, decimal ConsultationFee);
