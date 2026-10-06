using System.Text.RegularExpressions;

namespace SGCM.Data.Domain.Common;

public static partial class TemplatePlaceholders
{
    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    public static partial Regex Pattern();

    public static IReadOnlyCollection<string> Extract(string template) =>
        Pattern().Matches(template)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
}