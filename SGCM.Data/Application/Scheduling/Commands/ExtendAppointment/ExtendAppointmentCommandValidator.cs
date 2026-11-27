using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;

/// <summary>
/// Valida la forma de <see cref="ExtendAppointmentCommand"/>: el identificador de la cita debe ser mayor que cero y
/// las horas a sumar deben estar entre 1 y <see cref="MaximumAdditionalHours"/>.
/// </summary>
public sealed class ExtendAppointmentCommandValidator : AbstractValidator<ExtendAppointmentCommand>
{
    /// <summary>
    /// Máximo de horas que se pueden sumar de una vez. Ningún bloque dura más de un día, así que más horas nunca
    /// cabrían, y el tope evita desbordar la fecha al sumarlas al fin de la cita.
    /// </summary>
    public const int MaximumAdditionalHours = 24;

    public ExtendAppointmentCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");

        RuleFor(command => command.AdditionalHours)
            .InclusiveBetween(1, MaximumAdditionalHours)
            .WithErrorCode("Appointment.InvalidAdditionalHours")
            .WithMessage($"Las horas a sumar deben estar entre 1 y {MaximumAdditionalHours}.");
    }
}
