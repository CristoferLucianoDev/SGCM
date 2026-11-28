using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IDoctorPatientRelationshipReader"/> con una consulta directa a
/// <c>scheduling.appointment</c>.
/// </summary>
internal sealed class DoctorPatientRelationshipReader(AppDbContext context) : IDoctorPatientRelationshipReader
{
    public async Task<bool> HasAppointmentWithPatientAsync(
        int doctorId, int patientId, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM scheduling.appointment WHERE doctor_id = {doctorId} AND patient_id = {patientId}
                ) AS "Value"
                """)
            .SingleAsync(cancellationToken);
}
