using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Motivo de la cancelación o del rechazo de una cita. La cita lo guarda como texto; aquí solo se valida su
/// longitud y se cuentan sus palabras, que es lo que exigen las reglas del médico con poco aviso.
/// </summary>
public sealed record StatusReason
{
    /// <summary>Longitud máxima del motivo, igual que en la base.</summary>
    public const int MaxLength = 500;

    private StatusReason(string value)
    {
        Value = value;
        WordCount = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>Texto del motivo, ya recortado.</summary>
    public string Value { get; }

    /// <summary>Cantidad de palabras, separadas por cualquier espacio en blanco y sin contar las vacías.</summary>
    public int WordCount { get; }

    /// <summary>Crea un motivo obligatorio.</summary>
    /// <param name="value">Texto escrito por quien cancela o rechaza; se recorta.</param>
    /// <returns>
    /// El motivo, <see cref="AppointmentErrors.StatusReasonRequired"/> si es nulo o está en blanco, o
    /// <see cref="AppointmentErrors.StatusReasonTooLong"/> si recortado supera <see cref="MaxLength"/>.
    /// </returns>
    public static Result<StatusReason> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return AppointmentErrors.StatusReasonRequired;

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength) return AppointmentErrors.StatusReasonTooLong;

        return new StatusReason(trimmed);
    }

    /// <summary>Crea un motivo opcional: si no hay texto, el resultado es exitoso y sin motivo.</summary>
    /// <param name="value">Texto escrito por quien cancela; puede ser nulo o estar en blanco.</param>
    /// <returns>
    /// El motivo, <c>null</c> si no hay texto, o <see cref="AppointmentErrors.StatusReasonTooLong"/> si recortado
    /// supera <see cref="MaxLength"/>.
    /// </returns>
    public static Result<StatusReason?> CreateOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result<StatusReason?>.Success(null);

        var created = Create(value);
        return created.IsSuccess
            ? Result<StatusReason?>.Success(created.Value)
            : Result<StatusReason?>.Failure(created.Error);
    }

    /// <summary>Indica si el motivo tiene al menos esa cantidad de palabras.</summary>
    /// <param name="minimumWords">Mínimo de palabras exigido.</param>
    public bool HasAtLeastWords(int minimumWords) => WordCount >= minimumWords;
}
