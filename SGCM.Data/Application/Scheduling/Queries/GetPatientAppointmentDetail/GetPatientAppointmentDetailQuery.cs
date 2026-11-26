using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;

/// <summary>
/// Detalle de una cita propia del paciente autenticado.
/// </summary>
/// <param name="AppointmentId">Id de la cita.</param>
public sealed record GetPatientAppointmentDetailQuery(int AppointmentId) : IQuery<PatientAppointmentDetailDto>;
