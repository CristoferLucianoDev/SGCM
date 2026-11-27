using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Commands.CompleteFinishedAppointments;

/// <summary>
/// Completa las citas confirmadas que ya terminaron sin que el médico las cerrara. Lo ejecuta un proceso
/// automático y devuelve la cantidad de citas completadas.
/// </summary>
public sealed record CompleteFinishedAppointmentsCommand : ICommand<int>;
