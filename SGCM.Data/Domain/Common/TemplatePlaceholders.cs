using System.Text.RegularExpressions;

namespace SGCM.Data.Domain.Common;

public static partial class TemplatePlaceholders
{
    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    public static partial Regex Pattern();

    // Cualquier cosa entre llaves, como la detecta ck_reminder_template_variables: así "{Patient Name}" también se
    // rechaza aunque no sea un nombre de variable válido.
    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex AnyBraces();

    /// <summary>
    /// Devuelve, en orden de aparición y sin repetir, lo que hay entre llaves y no está en <paramref name="allowed"/>.
    /// </summary>
    public static IReadOnlyList<string> FindUnknown(string template, IReadOnlyCollection<string> allowed) =>
        AnyBraces().Matches(template)
            .Select(match => match.Groups[1].Value)
            .Where(name => !allowed.Contains(name))
            .Distinct()
            .ToArray();

    public static IReadOnlyCollection<string> Extract(string template) =>
        Pattern().Matches(template)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
}