using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;

/// <summary>
/// Valida la forma de <see cref="GetPatientAppointmentDetailQuery"/>: el identificador de la cita debe ser mayor
/// que cero.
/// </summary>
public sealed class GetPatientAppointmentDetailQueryValidator : AbstractValidator<GetPatientAppointmentDetailQuery>
{
    public GetPatientAppointmentDetailQueryValidator()
    {
        RuleFor(query => query.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");
    }
}
