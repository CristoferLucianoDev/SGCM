using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IAppointmentNotificationReader"/> con una consulta directa. La hora se convierte
/// con la zona de <see cref="SchedulingOptions"/>, la misma que usa la agenda.
/// </summary>
internal sealed class AppointmentNotificationReader(AppDbContext context, IOptions<SchedulingOptions> options)
    : IAppointmentNotificationReader
{
    public async Task<AppointmentNotificationData?> GetAsync(int appointmentId, CancellationToken cancellationToken)
    {
        var row = await context.Database.SqlQuery<AppointmentNotificationRow>($"""
            SELECT appointment.id,
                   patient.first_name AS patient_first_name,
                   patient.last_name AS patient_last_name,
                   patient_user.email AS patient_email,
                   doctor.first_name AS doctor_first_name,
                   doctor.last_name AS doctor_last_name,
                   doctor_user.email AS doctor_email,
                   specialty.name AS specialty_name,
                   doctor.consultation_fee,
                   appointment.starts_at,
                   appointment.ends_at,
                   appointment.reason_for_visit
            FROM scheduling.appointment appointment
            JOIN people.patient patient ON patient.id = appointment.patient_id
            JOIN app."user" patient_user ON patient_user.id = patient.user_id
            JOIN people.doctor doctor ON doctor.id = appointment.doctor_id
            JOIN app."user" doctor_user ON doctor_user.id = doctor.user_id
            JOIN people.specialty specialty ON specialty.id = appointment.specialty_id
            WHERE appointment.id = {appointmentId}
            """).SingleOrDefaultAsync(cancellationToken);

        if (row is null) return null;

        var calendar = BusinessCalendar.ForTimeZone(options.Value.TimeZone);
        var start = calendar.ToLocal(row.StartsAt);
        var end = calendar.ToLocal(row.EndsAt);

        return new AppointmentNotificationData(
            row.Id,
            FullName(row.PatientFirstName, row.PatientLastName),
            row.PatientEmail,
            FullName(row.DoctorFirstName, row.DoctorLastName),
            row.DoctorEmail,
            row.SpecialtyName,
            row.ConsultationFee,
            DateOnly.FromDateTime(start.DateTime),
            TimeOnly.FromDateTime(start.DateTime),
            TimeOnly.FromDateTime(end.DateTime),
            row.ReasonForVisit);
    }

    private static string FullName(string firstName, string lastName) => $"{firstName.Trim()} {lastName.Trim()}";
}

/// <summary>Fila de la consulta; los nombres siguen la convención snake_case de las columnas.</summary>
internal sealed record AppointmentNotificationRow(
    int Id,
    string PatientFirstName,
    string PatientLastName,
    string PatientEmail,
    string DoctorFirstName,
    string DoctorLastName,
    string DoctorEmail,
    string SpecialtyName,
    decimal ConsultationFee,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? ReasonForVisit);
