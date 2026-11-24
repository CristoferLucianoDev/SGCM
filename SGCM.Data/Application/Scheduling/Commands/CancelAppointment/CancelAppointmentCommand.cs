using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.CancelAppointment;

/// <summary>
/// El paciente autenticado cancela una cita propia que está pendiente o confirmada.
/// </summary>
/// <param name="AppointmentId">Cita que se cancela.</param>
/// <param name="Reason">Motivo de la cancelación; opcional.</param>
public sealed record CancelAppointmentCommand(int AppointmentId, string? Reason) : ICommand<Unit>;
