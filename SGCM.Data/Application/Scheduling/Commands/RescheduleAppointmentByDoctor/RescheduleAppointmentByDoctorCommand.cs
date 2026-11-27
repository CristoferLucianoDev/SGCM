using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.RescheduleAppointmentByDoctor;

/// <summary>
/// El médico autenticado reprograma una cita propia pendiente o confirmada que todavía no empezó a otra hora, en un
/// horario libre de sus bloques. La cita queda confirmada y el paciente recibe el aviso.
/// </summary>
/// <param name="AppointmentId">Cita que se reprograma.</param>
/// <param name="StartsAt">Nueva hora de inicio con su desfase; es obligatoria y la valida el validador.</param>
public sealed record RescheduleAppointmentByDoctorCommand(int AppointmentId, DateTimeOffset? StartsAt) : ICommand<Unit>;
