using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;

/// <summary>
/// El médico autenticado extiende una cita propia confirmada que está en curso, sumando horas enteras a su fin.
/// </summary>
/// <param name="AppointmentId">Cita que se extiende.</param>
/// <param name="AdditionalHours">Horas enteras que se suman al fin actual, entre 1 y el máximo permitido.</param>
public sealed record ExtendAppointmentCommand(int AppointmentId, int AdditionalHours) : ICommand<Unit>;
