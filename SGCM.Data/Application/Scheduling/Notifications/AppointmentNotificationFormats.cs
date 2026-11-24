using System.Globalization;

namespace SGCM.Data.Application.Scheduling.Notifications;

/// <summary>
/// Formatos de fecha y hora que comparten los correos de citas.
/// </summary>
public static class AppointmentNotificationFormats
{
    private const string DateFormat = "dd/MM/yyyy";
    private const string TimeFormat = "HH:mm";

    /// <summary>Da formato a la fecha como dd/MM/yyyy.</summary>
    /// <param name="date">Fecha local de la agenda.</param>
    public static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Da formato a la hora en 24 horas, como HH:mm.</summary>
    /// <param name="time">Hora local de la agenda.</param>
    public static string Time(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);
}
