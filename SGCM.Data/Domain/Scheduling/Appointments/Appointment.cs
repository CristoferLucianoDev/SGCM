using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Data.Domain.Scheduling.Availability;
// La propiedad StatusReason de la cita oculta el tipo del mismo nombre dentro de esta clase.
using StatusReasonValue = SGCM.Data.Domain.Scheduling.Appointments.StatusReason;

namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Cita de un paciente con un médico para una de sus especialidades. Que no se traslape con otra cita activa
/// del médico ni del paciente, y que quepa en un bloque abierto del médico, lo garantiza la base.
/// </summary>
public sealed class Appointment : AggregateRoot
{
    /// <summary>Longitud máxima del motivo de la consulta, igual que en la base.</summary>
    public const int ReasonForVisitMaxLength = 500;

    /// <summary>Longitud máxima del motivo de cancelación o rechazo, igual que en la base.</summary>
    public const int StatusReasonMaxLength = StatusReasonValue.MaxLength;

    /// <summary>Motivo con que se guarda la cita pendiente que el médico no confirmó a tiempo.</summary>
    public const string ExpiredReason = "No confirmada por el médico a tiempo";

    /// <summary>Horas de la ventana de inicio con que se consulta la extensión: solo cabe el inicio de la cita.</summary>
    private const int ExtensionStartWindowHours = 1;

    // Los ids reales son positivos y una cita sin guardar tiene Id 0: -1 nunca coincide con una exclusión.
    private const int UnrelatedIntervalId = -1;

    public int PatientId { get; private set; }

    public int DoctorId { get; private set; }

    public int SpecialtyId { get; private set; }

    /// <summary>Inicio en UTC, en punto.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Fin en UTC.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    /// <summary>Motivo de la consulta que escribió el paciente; <c>null</c> si no escribió ninguno.</summary>
    public string? ReasonForVisit { get; private set; }

    /// <summary>Motivo de la cancelación o del rechazo; <c>null</c> mientras la cita está activa.</summary>
    public string? StatusReason { get; private set; }

    private Appointment()
    {
    }

    /// <summary>
    /// Crea la solicitud de cita del paciente: pendiente, en punto, con la duración de la configuración y con
    /// un inicio dentro de la ventana de reservas. Emite <see cref="AppointmentRequestedEvent"/>.
    /// </summary>
    /// <param name="reasonForVisit">Motivo opcional; se recorta y, si queda vacío, se guarda <c>null</c>.</param>
    /// <param name="durationHours">Duración de la cita en horas, según la configuración.</param>
    /// <param name="bookingWindow">
    /// Ventana en la que puede empezar la cita: su inicio es la antelación mínima y su fin, excluido, el horizonte.
    /// </param>
    /// <returns>La cita con las fechas en UTC, o el primer error de validación encontrado.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración no es positiva.</exception>
    /// <exception cref="ArgumentNullException">Si la ventana es nula.</exception>
    public static Result<Appointment> Request(
        int patientId,
        int doctorId,
        int specialtyId,
        DateTimeOffset startsAt,
        string? reasonForVisit,
        int durationHours,
        TimeRange bookingWindow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationHours);
        ArgumentNullException.ThrowIfNull(bookingWindow);

        if (patientId <= 0) return AppointmentErrors.InvalidPatient;
        if (doctorId <= 0) return AppointmentErrors.InvalidDoctor;
        if (specialtyId <= 0) return AppointmentErrors.InvalidSpecialty;
        var start = ValidateStart(startsAt, bookingWindow);
        if (start != Error.None) return start;

        var reason = string.IsNullOrWhiteSpace(reasonForVisit) ? null : reasonForVisit.Trim();
        if (reason?.Length > ReasonForVisitMaxLength) return AppointmentErrors.ReasonTooLong;

        var startUtc = startsAt.ToUniversalTime();
        var appointment = new Appointment
        {
            PatientId = patientId,
            DoctorId = doctorId,
            SpecialtyId = specialtyId,
            StartsAt = startUtc,
            EndsAt = startUtc.AddHours(durationHours),
            Status = AppointmentStatus.Pending,
            ReasonForVisit = reason
        };
        appointment.Raise(new AppointmentRequestedEvent(appointment));

        return appointment;
    }

    /// <summary>
    /// Cancela la cita si está pendiente o confirmada. El paciente solo puede dentro del plazo y con motivo
    /// opcional; el médico en cualquier momento, con motivo obligatorio y, si faltan pocas horas, con un mínimo
    /// de palabras. Emite <see cref="AppointmentCancelledEvent"/>; ante cualquier error la cita no cambia.
    /// </summary>
    /// <param name="cancelledBy">Quién cancela.</param>
    /// <param name="reason">Motivo; se recorta. Para el paciente es opcional y, si queda vacío, se guarda <c>null</c>.</param>
    /// <param name="now">Instante actual, que se compara con el plazo y con el umbral de poco aviso.</param>
    /// <param name="patientCancellationDeadlineHours">
    /// Horas de antelación mínima con que el paciente puede cancelar, según la configuración.
    /// </param>
    /// <param name="shortNotice">Regla de la cancelación del médico con poco aviso.</param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.InvalidTransition"/>, <see cref="AppointmentErrors.CancellationDeadlinePassed"/>
    /// (paciente), <see cref="AppointmentErrors.StatusReasonRequired"/> (médico),
    /// <see cref="AppointmentErrors.StatusReasonTooLong"/> o <see cref="AppointmentErrors.ShortNoticeReasonTooShort"/>
    /// (médico).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el plazo no es positivo o <paramref name="cancelledBy"/> no es válido.</exception>
    /// <exception cref="ArgumentNullException">Si <paramref name="shortNotice"/> es nulo.</exception>
    public Result Cancel(
        AppointmentCanceller cancelledBy,
        string? reason,
        DateTimeOffset now,
        int patientCancellationDeadlineHours,
        ShortNoticeCancellation shortNotice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(patientCancellationDeadlineHours);
        ArgumentNullException.ThrowIfNull(shortNotice);
        if (cancelledBy is not (AppointmentCanceller.Patient or AppointmentCanceller.Doctor))
            throw new ArgumentOutOfRangeException(nameof(cancelledBy));

        if (!IsActive()) return AppointmentErrors.InvalidTransition;

        var validated = cancelledBy == AppointmentCanceller.Patient
            ? ValidatePatientCancellation(reason, now, patientCancellationDeadlineHours)
            : ValidateDoctorCancellation(reason, now, shortNotice);
        if (validated.IsFailure) return validated.Error;

        Status = AppointmentStatus.Cancelled;
        StatusReason = validated.Value?.Value;
        Raise(new AppointmentCancelledEvent(this, cancelledBy));

        return Result.Success();
    }

    private Result<StatusReasonValue?> ValidatePatientCancellation(string? reason, DateTimeOffset now, int deadlineHours)
    {
        if (!IsWithinDeadline(now, deadlineHours)) return AppointmentErrors.CancellationDeadlinePassed;

        return StatusReasonValue.CreateOptional(reason);
    }

    /// <summary>
    /// Confirma la cita si está pendiente y aún está dentro del plazo para confirmar. Emite
    /// <see cref="AppointmentConfirmedEvent"/>; ante cualquier error la cita no cambia.
    /// </summary>
    /// <param name="now">Instante actual, que se compara con el plazo.</param>
    /// <param name="confirmationDeadlineHours">
    /// Horas de antelación mínima con que el médico puede confirmar, según la configuración.
    /// </param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.NotPending"/> si la cita no está pendiente, o
    /// <see cref="AppointmentErrors.ConfirmationDeadlinePassed"/> si el plazo ya pasó.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el plazo no es positivo.</exception>
    public Result Confirm(DateTimeOffset now, int confirmationDeadlineHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(confirmationDeadlineHours);

        var notAwaiting = ValidateAwaitingConfirmation(now, confirmationDeadlineHours);
        if (notAwaiting != Error.None) return notAwaiting;

        Status = AppointmentStatus.Confirmed;
        Raise(new AppointmentConfirmedEvent(this));

        return Result.Success();
    }

    /// <summary>
    /// Rechaza la cita si está pendiente y aún está dentro del plazo para confirmar o rechazar, con un motivo
    /// obligatorio y sin mínimo de palabras. Emite <see cref="AppointmentRejectedEvent"/>; ante cualquier error la
    /// cita no cambia.
    /// </summary>
    /// <param name="reason">Motivo del rechazo; se recorta y es obligatorio.</param>
    /// <param name="now">Instante actual, que se compara con el plazo.</param>
    /// <param name="confirmationDeadlineHours">
    /// Horas de antelación mínima con que el médico puede rechazar, según la configuración.
    /// </param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.NotPending"/> si la cita no está pendiente,
    /// <see cref="AppointmentErrors.ConfirmationDeadlinePassed"/> si el plazo ya pasó,
    /// <see cref="AppointmentErrors.StatusReasonRequired"/> si falta el motivo o
    /// <see cref="AppointmentErrors.StatusReasonTooLong"/> si supera la longitud máxima. Se validan en ese orden.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el plazo no es positivo.</exception>
    public Result Reject(string? reason, DateTimeOffset now, int confirmationDeadlineHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(confirmationDeadlineHours);

        var notAwaiting = ValidateAwaitingConfirmation(now, confirmationDeadlineHours);
        if (notAwaiting != Error.None) return notAwaiting;

        var validated = StatusReasonValue.Create(reason);
        if (validated.IsFailure) return validated.Error;

        Status = AppointmentStatus.Rejected;
        StatusReason = validated.Value.Value;
        Raise(new AppointmentRejectedEvent(this));

        return Result.Success();
    }

    /// <summary>
    /// Vence la cita pendiente que el médico no confirmó a tiempo: pasa a cancelada con el motivo
    /// <see cref="ExpiredReason"/> y emite <see cref="AppointmentExpiredEvent"/>. Ante cualquier error la cita no
    /// cambia ni emite eventos.
    /// </summary>
    /// <param name="now">Instante actual, que se compara con el plazo.</param>
    /// <param name="confirmationDeadlineHours">
    /// Horas de antelación mínima con que el médico puede confirmar, según la configuración.
    /// </param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.NotPending"/> si la cita no está pendiente, o
    /// <see cref="AppointmentErrors.ConfirmationDeadlineNotReached"/> si todavía puede confirmarse.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el plazo no es positivo.</exception>
    public Result Expire(DateTimeOffset now, int confirmationDeadlineHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(confirmationDeadlineHours);

        if (Status != AppointmentStatus.Pending) return AppointmentErrors.NotPending;
        // Es la negación exacta de Confirm: una pendiente vence justo cuando ya no puede confirmarse.
        if (IsAwaitingConfirmation(now, confirmationDeadlineHours)) return AppointmentErrors.ConfirmationDeadlineNotReached;

        Status = AppointmentStatus.Cancelled;
        StatusReason = ExpiredReason;
        Raise(new AppointmentExpiredEvent(this));

        return Result.Success();
    }

    /// <summary>
    /// Reprograma la cita a otra hora por pedido del paciente: dentro del plazo de reprogramación y de la ventana de
    /// reservas, queda pendiente con el nuevo inicio y el fin según la duración. Emite
    /// <see cref="AppointmentRescheduledEvent"/>; ante cualquier error la cita no cambia ni emite eventos. Que el
    /// horario esté libre y dentro de un bloque abierto del médico lo comprueba quien llama.
    /// </summary>
    /// <param name="newStartsAt">Nuevo inicio; se guarda en UTC.</param>
    /// <param name="now">Instante actual, que se compara con el plazo.</param>
    /// <param name="patientRescheduleDeadlineHours">
    /// Horas de antelación mínima con que el paciente puede reprogramar, según la configuración.
    /// </param>
    /// <param name="durationHours">Duración de la cita en horas, según la configuración.</param>
    /// <param name="bookingWindow">
    /// Ventana en la que puede empezar la cita: su inicio es la antelación mínima y su fin, excluido, el horizonte.
    /// </param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.NotActive"/> si la cita no está pendiente ni confirmada,
    /// <see cref="AppointmentErrors.RescheduleDeadlinePassed"/> si el plazo ya pasó,
    /// <see cref="AppointmentErrors.NotOnHour"/>, <see cref="AppointmentErrors.TooSoon"/> o
    /// <see cref="AppointmentErrors.BeyondHorizon"/> si la nueva hora no es válida, o
    /// <see cref="AppointmentErrors.SameSchedule"/> si es el mismo inicio actual. Se validan en ese orden.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el plazo o la duración no son positivos.</exception>
    /// <exception cref="ArgumentNullException">Si la ventana es nula.</exception>
    public Result RescheduleByPatient(
        DateTimeOffset newStartsAt,
        DateTimeOffset now,
        int patientRescheduleDeadlineHours,
        int durationHours,
        TimeRange bookingWindow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(patientRescheduleDeadlineHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationHours);
        ArgumentNullException.ThrowIfNull(bookingWindow);

        var relocation = ValidatePatientRelocation(newStartsAt, now, patientRescheduleDeadlineHours, bookingWindow);
        if (relocation != Error.None) return relocation;

        var newStartUtc = newStartsAt.ToUniversalTime();
        // Reprogramar a la misma hora no cambia nada; el cambio de médico sí puede conservarla, por eso va aparte.
        if (newStartUtc == StartsAt) return AppointmentErrors.SameSchedule;

        var previousStartsAt = StartsAt;
        StartsAt = newStartUtc;
        EndsAt = newStartUtc.AddHours(durationHours);
        Status = AppointmentStatus.Pending;
        Raise(new AppointmentRescheduledEvent(this, AppointmentParticipant.Patient, previousStartsAt));

        return Result.Success();
    }

    /// <summary>
    /// Validación que comparten la reprogramación del paciente y el cambio de médico: la cita debe estar activa, el
    /// paciente dentro de su plazo de reprogramación y la nueva hora en punto y dentro de la ventana de reservas.
    /// Reutiliza <see cref="IsActive"/>, <see cref="IsWithinDeadline"/> y <see cref="ValidateStart"/>, las mismas reglas
    /// que usan las acciones permitidas y la solicitud de cita.
    /// </summary>
    /// <returns>
    /// <see cref="Error.None"/>, <see cref="AppointmentErrors.NotActive"/>,
    /// <see cref="AppointmentErrors.RescheduleDeadlinePassed"/> o el error de <see cref="ValidateStart"/>.
    /// </returns>
    private Error ValidatePatientRelocation(
        DateTimeOffset newStartsAt, DateTimeOffset now, int deadlineHours, TimeRange bookingWindow)
    {
        if (!IsActive()) return AppointmentErrors.NotActive;
        if (!IsWithinDeadline(now, deadlineHours)) return AppointmentErrors.RescheduleDeadlinePassed;

        return ValidateStart(newStartsAt, bookingWindow);
    }

    /// <summary>
    /// Extiende la cita en curso las horas indicadas, si la cita completa, desde su inicio hasta el nuevo fin, cabe
    /// en UN solo bloque abierto del médico y el tramo añadido no se cruza con otra cita activa. No emite eventos;
    /// ante cualquier error la cita no cambia.
    /// </summary>
    /// <param name="additionalHours">Horas que se añaden al fin previsto.</param>
    /// <param name="now">Instante actual, que decide si la cita está en curso.</param>
    /// <param name="openBlocks">
    /// Bloques abiertos del médico; los lee quien llama y deben cubrir desde el inicio de la cita, no solo el tramo
    /// añadido, porque la base exige que la cita completa quepa en un solo bloque.
    /// </param>
    /// <param name="otherActiveAppointments">
    /// Rangos de las demás citas activas del médico; los lee quien llama y no debe incluir esta cita.
    /// </param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.NotInProgress"/> si no es una cita confirmada en curso, o
    /// <see cref="AppointmentErrors.ExtensionUnavailable"/> si la cita extendida no cabe en un bloque o el nuevo tramo
    /// no está libre. Se validan en ese orden.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Si las horas no son positivas.</exception>
    /// <exception cref="ArgumentNullException">Si alguna colección es nula.</exception>
    public Result Extend(
        int additionalHours,
        DateTimeOffset now,
        IReadOnlyCollection<TimeRange> openBlocks,
        IReadOnlyCollection<TimeRange> otherActiveAppointments)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(additionalHours);
        ArgumentNullException.ThrowIfNull(openBlocks);
        ArgumentNullException.ThrowIfNull(otherActiveAppointments);

        if (!IsInProgress(now)) return AppointmentErrors.NotInProgress;
        if (!IsExtensionFree(additionalHours, openBlocks, otherActiveAppointments))
            return AppointmentErrors.ExtensionUnavailable;

        EndsAt = EndsAt.AddHours(additionalHours);

        return Result.Success();
    }

    /// <summary>
    /// Termina la cita en curso: queda completada, con la hora actual como fin real, y emite
    /// <see cref="AppointmentCompletedEvent"/>. Ante cualquier error la cita no cambia ni emite eventos.
    /// </summary>
    /// <param name="now">Instante actual, que decide si la cita está en curso y pasa a ser su fin real.</param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.NotInProgress"/> si no es una cita confirmada en curso o si es justo el
    /// instante de inicio, porque el fin quedaría igual al inicio y la base lo rechaza.
    /// </returns>
    public Result Complete(DateTimeOffset now)
    {
        if (!CanComplete(now)) return AppointmentErrors.NotInProgress;

        EndsAt = now.ToUniversalTime();
        MarkCompleted();

        return Result.Success();
    }

    /// <summary>
    /// Completa la cita confirmada que ya llegó a su fin sin que el médico la cerrara; lo usa el proceso automático.
    /// El fin previsto se conserva y se emite <see cref="AppointmentCompletedEvent"/>. Ante cualquier error la cita no
    /// cambia ni emite eventos.
    /// </summary>
    /// <param name="now">Instante actual, que se compara con el fin de la cita.</param>
    /// <returns>
    /// Éxito, o <see cref="AppointmentErrors.NotConfirmed"/> si la cita no está confirmada, o
    /// <see cref="AppointmentErrors.NotFinished"/> si todavía no llegó a su fin. Se validan en ese orden.
    /// </returns>
    public Result CompleteFinished(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Confirmed) return AppointmentErrors.NotConfirmed;
        if (!HasReachedEnd(now)) return AppointmentErrors.NotFinished;

        MarkCompleted();

        return Result.Success();
    }

    // Cambio de estado y evento comunes a las dos formas de completar una cita.
    private void MarkCompleted()
    {
        Status = AppointmentStatus.Completed;
        Raise(new AppointmentCompletedEvent(this));
    }

    // El trigger T3 exige que la cita COMPLETA [StartsAt, nuevo fin) quepa en UN solo bloque, no solo el tramo nuevo.
    // La ventana abarca una sola hora para que el único inicio posible sea el de la cita: así Calculate responde en
    // una llamada si la cita ya extendida cabe en un bloque y está libre, sin duplicar su lógica aquí. Las horas
    // son enteras porque la cita empieza y termina en punto mientras está confirmada.
    private bool IsExtensionFree(
        int additionalHours, IReadOnlyCollection<TimeRange> openBlocks, IReadOnlyCollection<TimeRange> otherActiveAppointments)
    {
        var startWindow = TimeRange.Create(StartsAt, StartsAt.AddHours(ExtensionStartWindowHours)).Value;
        // Si la duración no fuera de horas enteras, redondear hacia arriba revisa de más en vez de dejar un tramo sin revisar.
        var extendedHours = (int)Math.Ceiling((EndsAt - StartsAt).TotalHours) + additionalHours;
        var blockers = otherActiveAppointments.Select(range => new BookedInterval(UnrelatedIntervalId, range));

        return FreeSlotCalculator.Calculate(openBlocks, blockers, startWindow, extendedHours).Contains(StartsAt);
    }

    // Confirmar y rechazar comparten estado y plazo: se apoya en IsAwaitingConfirmation, la misma regla que usan
    // las acciones permitidas, y solo distingue el error para informar si falló el estado o el plazo.
    private Error ValidateAwaitingConfirmation(DateTimeOffset now, int confirmationDeadlineHours)
    {
        if (IsAwaitingConfirmation(now, confirmationDeadlineHours)) return Error.None;

        return Status == AppointmentStatus.Pending
            ? AppointmentErrors.ConfirmationDeadlinePassed
            : AppointmentErrors.NotPending;
    }

    /// <summary>
    /// Calcula qué acciones tiene disponibles quien consulta la cita en ese instante, y los datos que las acompañan.
    /// Solo decide la disponibilidad: ejecutar cada acción es responsabilidad de su propio método.
    /// </summary>
    /// <param name="viewer">Quién consulta la cita.</param>
    /// <param name="now">Instante actual, que se compara con los plazos.</param>
    /// <param name="rules">Plazos y regla de poco aviso de la configuración.</param>
    /// <returns>Las acciones en el orden de <see cref="AppointmentAction"/> y los datos de plazo.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="rules"/> es nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="viewer"/> no es válido.</exception>
    public AllowedAppointmentActions GetAllowedActions(
        AppointmentParticipant viewer, DateTimeOffset now, AppointmentRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var actions = viewer switch
        {
            AppointmentParticipant.Patient => PatientActions(now, rules),
            AppointmentParticipant.Doctor => DoctorActions(now, rules),
            _ => throw new ArgumentOutOfRangeException(nameof(viewer))
        };
        var requiresExtendedReason = viewer == AppointmentParticipant.Doctor
            && actions.Contains(AppointmentAction.Cancel)
            && rules.ShortNotice.AppliesTo(StartsAt, now);
        var (deadline, remaining) = ConfirmationWindow(now, rules);

        return new AllowedAppointmentActions(actions, requiresExtendedReason, deadline, remaining);
    }

    // Se agregan en el orden de AppointmentAction para que la lista salga ya ordenada.
    private List<AppointmentAction> PatientActions(DateTimeOffset now, AppointmentRules rules)
    {
        var actions = new List<AppointmentAction>();
        if (!IsActive()) return actions;

        if (IsWithinDeadline(now, rules.PatientCancellationDeadlineHours)) actions.Add(AppointmentAction.Cancel);
        if (IsWithinDeadline(now, rules.PatientRescheduleDeadlineHours))
        {
            actions.Add(AppointmentAction.Reschedule);
            actions.Add(AppointmentAction.ChangeDoctor);
        }

        return actions;
    }

    private List<AppointmentAction> DoctorActions(DateTimeOffset now, AppointmentRules rules)
    {
        var actions = new List<AppointmentAction>();
        if (IsActive()) actions.Add(AppointmentAction.Cancel);
        if (IsActive() && now < StartsAt) actions.Add(AppointmentAction.Reschedule);
        if (IsAwaitingConfirmation(now, rules.ConfirmationDeadlineHours))
        {
            actions.Add(AppointmentAction.Confirm);
            actions.Add(AppointmentAction.Reject);
        }

        if (IsInProgress(now))
        {
            actions.Add(AppointmentAction.Extend);
            if (CanComplete(now)) actions.Add(AppointmentAction.Complete);
        }

        return actions;
    }

    private bool IsAwaitingConfirmation(DateTimeOffset now, int confirmationDeadlineHours) =>
        Status == AppointmentStatus.Pending && IsWithinDeadline(now, confirmationDeadlineHours);

    private bool IsInProgress(DateTimeOffset now) =>
        Status == AppointmentStatus.Confirmed && StartsAt <= now && !HasReachedEnd(now);

    // El fin se excluye del rango de la cita: en ese instante ya no está en curso y puede completarse sola.
    private bool HasReachedEnd(DateTimeOffset now) => now >= EndsAt;

    // Terminar justo en el inicio dejaría ends_at igual a starts_at, que la base rechaza.
    private bool CanComplete(DateTimeOffset now) => IsInProgress(now) && now > StartsAt;

    private (DateTimeOffset? Deadline, TimeSpan? Remaining) ConfirmationWindow(DateTimeOffset now, AppointmentRules rules)
    {
        if (Status != AppointmentStatus.Pending) return (null, null);

        var deadline = StartsAt.AddHours(-rules.ConfirmationDeadlineHours);
        var remaining = deadline - now;

        return (deadline, remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining);
    }

    // Activa es la cita pendiente o confirmada: las demás están en un estado final.
    private bool IsActive() => Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;

    // El plazo se cumple hasta el instante límite inclusive: StartsAt menos las horas de antelación exigidas.
    private bool IsWithinDeadline(DateTimeOffset now, int deadlineHours) => now <= StartsAt.AddHours(-deadlineHours);

    private Result<StatusReasonValue?> ValidateDoctorCancellation(
        string? reason, DateTimeOffset now, ShortNoticeCancellation shortNotice)
    {
        var created = StatusReasonValue.Create(reason);
        if (created.IsFailure) return created.Error;

        if (shortNotice.AppliesTo(StartsAt, now) && !created.Value.HasAtLeastWords(shortNotice.MinimumWords))
            return AppointmentErrors.ShortNoticeReasonTooShort(shortNotice.MinimumWords);

        return Result<StatusReasonValue?>.Success(created.Value);
    }

    /// <summary>
    /// Comprueba que la cita pueda empezar en ese instante: en punto y dentro de la ventana de reservas. Permite
    /// rechazar el horario antes de buscar o bloquear al médico, con el mismo error que <see cref="Request"/>.
    /// </summary>
    /// <returns>
    /// <see cref="Error.None"/>, <see cref="AppointmentErrors.NotOnHour"/>, <see cref="AppointmentErrors.TooSoon"/>
    /// o <see cref="AppointmentErrors.BeyondHorizon"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Si la ventana es nula.</exception>
    public static Error ValidateStart(DateTimeOffset startsAt, TimeRange bookingWindow)
    {
        ArgumentNullException.ThrowIfNull(bookingWindow);

        if (!HourAlignedInstant.IsOnHour(startsAt)) return AppointmentErrors.NotOnHour;
        if (startsAt < bookingWindow.Start) return AppointmentErrors.TooSoon;
        if (startsAt >= bookingWindow.End) return AppointmentErrors.BeyondHorizon;

        return Error.None;
    }
}
