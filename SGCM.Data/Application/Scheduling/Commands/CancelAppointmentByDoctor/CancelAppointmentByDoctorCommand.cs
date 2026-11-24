using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;

/// <summary>
/// El médico autenticado cancela una cita propia pendiente o confirmada, en cualquier momento y con motivo
/// obligatorio; con poco aviso el motivo exige un mínimo de palabras.
/// </summary>
/// <param name="AppointmentId">Cita que se cancela.</param>
/// <param name="Reason">Motivo de la cancelación; obligatorio.</param>
public sealed record CancelAppointmentByDoctorCommand(int AppointmentId, string? Reason) : ICommand<Unit>;
