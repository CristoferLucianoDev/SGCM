using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;

/// <summary>
/// Devuelve el detalle de una cita de la agenda del médico de la sesión. Una cita de otro médico, o que ya no está en
/// la agenda, responde como inexistente para no revelar citas ajenas.
/// </summary>
internal sealed class GetDoctorAppointmentDetailQueryHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IDoctorScheduleReader reader,
    IValidator<GetDoctorAppointmentDetailQuery> validator) : IQueryHandler<GetDoctorAppointmentDetailQuery, DoctorAppointmentDetailDto>
{
    public async Task<Result<DoctorAppointmentDetailDto>> HandleAsync(
        GetDoctorAppointmentDetailQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor)) return DoctorErrors.NotFound;
        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        var detail = await reader.GetDetailAsync(doctorId.Value, query.AppointmentId, cancellationToken);
        return detail is null ? AppointmentErrors.NotFound : detail;
    }
}
