namespace SGCM.Data.Application.Abstractions.Notifications;

public interface ITemplateRenderer
{
    string Render(string template, IReadOnlyDictionary<string, string> values, bool htmlEncode);
}