using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.RejectAppointment;

/// <summary>
/// El médico autenticado rechaza una cita propia pendiente, hasta el plazo de confirmación configurado y con
/// motivo obligatorio.
/// </summary>
/// <param name="AppointmentId">Cita que se rechaza.</param>
/// <param name="Reason">Motivo del rechazo; obligatorio.</param>
public sealed record RejectAppointmentCommand(int AppointmentId, string? Reason) : ICommand<Unit>;
