namespace SGCM.Data.Domain.Common;

/// <summary>
/// Cambio opcional de un campo en una actualización parcial. Distingue "no enviado" de "enviado con
/// valor nulo", algo que un simple <c>null</c> no puede expresar: un campo no enviado conserva su valor,
/// mientras que uno enviado nulo puede, según el campo, quitarlo o rechazarse.
/// </summary>
/// <typeparam name="T">Tipo del valor del campo.</typeparam>
/// <param name="IsProvided">Indica si el campo fue enviado en la actualización.</param>
/// <param name="Value">Valor enviado; solo tiene sentido si <paramref name="IsProvided"/> es <c>true</c>.</param>
public readonly record struct Change<T>(bool IsProvided, T? Value)
{
    /// <summary>Campo no enviado: la actualización conserva su valor actual. Equivale a <c>default</c>.</summary>
    public static Change<T> Unchanged => default;

    /// <summary>Campo enviado con el valor indicado, que puede ser <c>null</c>.</summary>
    /// <param name="value">Valor nuevo del campo.</param>
    /// <returns>Un cambio marcado como enviado.</returns>
    public static Change<T> Set(T? value) => new(true, value);
}
