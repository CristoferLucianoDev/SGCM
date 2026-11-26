namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Plazos y reglas que deciden qué acciones están disponibles sobre una cita. Los valores salen de la
/// configuración de la agenda.
/// </summary>
public sealed record AppointmentRules
{
    /// <summary>Crea las reglas con los valores de la configuración.</summary>
    /// <param name="patientCancellationDeadlineHours">Horas de antelación mínima para que el paciente cancele.</param>
    /// <param name="patientRescheduleDeadlineHours">
    /// Horas de antelación mínima para que el paciente reprograme o cambie de médico.
    /// </param>
    /// <param name="confirmationDeadlineHours">Horas de antelación mínima para que el médico confirme o rechace.</param>
    /// <param name="shortNotice">Regla de la cancelación del médico con poco aviso.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguno de los plazos no es positivo.</exception>
    /// <exception cref="ArgumentNullException">Si <paramref name="shortNotice"/> es nulo.</exception>
    public AppointmentRules(
        int patientCancellationDeadlineHours,
        int patientRescheduleDeadlineHours,
        int confirmationDeadlineHours,
        ShortNoticeCancellation shortNotice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(patientCancellationDeadlineHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(patientRescheduleDeadlineHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(confirmationDeadlineHours);
        ArgumentNullException.ThrowIfNull(shortNotice);

        PatientCancellationDeadlineHours = patientCancellationDeadlineHours;
        PatientRescheduleDeadlineHours = patientRescheduleDeadlineHours;
        ConfirmationDeadlineHours = confirmationDeadlineHours;
        ShortNotice = shortNotice;
    }

    /// <summary>Horas de antelación mínima para que el paciente cancele.</summary>
    public int PatientCancellationDeadlineHours { get; }

    /// <summary>Horas de antelación mínima para que el paciente reprograme o cambie de médico.</summary>
    public int PatientRescheduleDeadlineHours { get; }

    /// <summary>Horas de antelación mínima para que el médico confirme o rechace.</summary>
    public int ConfirmationDeadlineHours { get; }

    /// <summary>Regla de la cancelación del médico con poco aviso.</summary>
    public ShortNoticeCancellation ShortNotice { get; }
}
