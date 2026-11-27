using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAppointment;

/// <summary>
/// El paciente autenticado reprograma una cita propia pendiente o confirmada a otra hora, con el mismo médico, hasta
/// el plazo de reprogramación configurado. La cita vuelve a pendiente.
/// </summary>
/// <param name="AppointmentId">Cita que se reprograma.</param>
/// <param name="StartsAt">Nueva hora de inicio con su desfase; es obligatoria y la valida el validador.</param>
public sealed record RescheduleAppointmentCommand(int AppointmentId, DateTimeOffset? StartsAt) : ICommand<Unit>;
