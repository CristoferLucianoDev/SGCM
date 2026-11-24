using FluentValidation;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.RequestAppointment;

/// <summary>
/// Valida la forma de <see cref="RequestAppointmentCommand"/>: los identificadores enviados deben ser mayores que
/// cero, el médico elegido y el asignado no van juntos y el motivo respeta su longitud máxima.
/// </summary>
public sealed class RequestAppointmentCommandValidator : AbstractValidator<RequestAppointmentCommand>
{
    public RequestAppointmentCommandValidator()
    {
        RuleFor(command => command.SpecialtyId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidSpecialtyId")
            .WithMessage("El identificador de la especialidad debe ser mayor que cero.");

        RuleFor(command => command.DoctorId)
            .GreaterThan(0)
            .When(command => command.DoctorId.HasValue)
            .WithErrorCode("Appointment.InvalidDoctorId")
            .WithMessage("El identificador del médico debe ser mayor que cero.");

        RuleFor(command => command.AssignedDoctorId)
            .GreaterThan(0)
            .When(command => command.AssignedDoctorId.HasValue)
            .WithErrorCode("Appointment.InvalidAssignedDoctorId")
            .WithMessage("El identificador del médico asignado debe ser mayor que cero.");

        RuleFor(command => command)
            .Must(command => command.DoctorId is null || command.AssignedDoctorId is null)
            .WithName(nameof(RequestAppointmentCommand.DoctorId))
            .WithErrorCode("Appointment.DoctorAndAssignedDoctor")
            .WithMessage("Envíe el médico elegido o el médico asignado, no ambos.");

        RuleFor(command => command.ProvinceId)
            .GreaterThan(0)
            .When(command => command.ProvinceId.HasValue)
            .WithErrorCode("Appointment.InvalidProvinceId")
            .WithMessage("El identificador de la provincia debe ser mayor que cero.");

        RuleFor(command => command.MunicipalityId)
            .GreaterThan(0)
            .When(command => command.MunicipalityId.HasValue)
            .WithErrorCode("Appointment.InvalidMunicipalityId")
            .WithMessage("El identificador del municipio debe ser mayor que cero.");

        RuleFor(command => command.ReasonForVisit)
            .Must(reason => reason is null || reason.Trim().Length <= Appointment.ReasonForVisitMaxLength)
            .WithErrorCode(AppointmentErrors.ReasonTooLong.Code)
            .WithMessage(AppointmentErrors.ReasonTooLong.Description);
    }
}
