using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;

/// <summary>
/// El médico autenticado confirma una cita propia pendiente, hasta el plazo de confirmación configurado.
/// </summary>
/// <param name="AppointmentId">Cita que se confirma.</param>
public sealed record ConfirmAppointmentCommand(int AppointmentId) : ICommand<Unit>;
