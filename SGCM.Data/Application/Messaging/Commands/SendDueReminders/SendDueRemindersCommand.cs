using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Messaging.Commands.SendDueReminders;

/// <summary>
/// Envía los recordatorios pendientes cuya hora llegó, automáticos, programados por el médico o de seguimiento.
/// Devuelve cuántos procesó, enviados o fallidos.
/// </summary>
public sealed record SendDueRemindersCommand : ICommand<int>;
