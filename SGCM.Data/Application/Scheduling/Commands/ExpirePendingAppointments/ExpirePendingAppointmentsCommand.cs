using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Commands.ExpirePendingAppointments;

/// <summary>
/// Vence las citas pendientes que llegaron al plazo de confirmación sin confirmarse. Lo ejecuta un proceso
/// automático y devuelve la cantidad de citas vencidas.
/// </summary>
public sealed record ExpirePendingAppointmentsCommand : ICommand<int>;
