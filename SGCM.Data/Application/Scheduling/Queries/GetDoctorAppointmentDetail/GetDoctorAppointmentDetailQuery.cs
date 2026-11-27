using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;

/// <summary>Detalle de una cita de la agenda del médico autenticado.</summary>
/// <param name="AppointmentId">Cita que se consulta.</param>
public sealed record GetDoctorAppointmentDetailQuery(int AppointmentId) : IQuery<DoctorAppointmentDetailDto>;
