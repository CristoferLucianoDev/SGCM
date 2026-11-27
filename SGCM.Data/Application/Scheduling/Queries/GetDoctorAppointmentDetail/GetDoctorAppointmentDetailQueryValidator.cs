using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;

public sealed class GetDoctorAppointmentDetailQueryValidator : AbstractValidator<GetDoctorAppointmentDetailQuery>
{
    public GetDoctorAppointmentDetailQueryValidator()
    {
        RuleFor(query => query.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");
    }
}
