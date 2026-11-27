using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.CompleteAppointment;

/// <summary>
/// El médico autenticado termina una cita propia confirmada que está en curso; la hora actual queda como su fin real.
/// </summary>
/// <param name="AppointmentId">Cita que se termina.</param>
public sealed record CompleteAppointmentCommand(int AppointmentId) : ICommand<Unit>;
