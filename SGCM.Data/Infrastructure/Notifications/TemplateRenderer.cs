using System.Net;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Infrastructure.Notifications;

internal sealed class TemplateRenderer : ITemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, string> values, bool htmlEncode) =>
        TemplatePlaceholders.Pattern().Replace(template, match =>
        {
            if (!values.TryGetValue(match.Groups[1].Value, out var value))
            {
                return match.Value;
            }

            return htmlEncode ? WebUtility.HtmlEncode(value) : value;
        });
}