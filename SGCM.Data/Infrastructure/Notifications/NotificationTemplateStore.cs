using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SGCM.Data.Infrastructure.Notifications;

internal sealed record NotificationTemplate(string Subject, string HtmlBody);

internal sealed partial class NotificationTemplateStore
{
    public const string DefaultResourcePrefix = "SGCM.Data.Infrastructure.Notifications.Templates.";

    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;
    private readonly ConcurrentDictionary<string, NotificationTemplate> _cache = new();

    public NotificationTemplateStore()
        : this(typeof(NotificationTemplateStore).Assembly, DefaultResourcePrefix)
    {
    }

    public NotificationTemplateStore(Assembly assembly, string resourcePrefix)
    {
        _assembly = assembly;
        _resourcePrefix = resourcePrefix;
    }

    public NotificationTemplate Get(string templateKey) => _cache.GetOrAdd(templateKey, Load);

    private NotificationTemplate Load(string templateKey)
    {
        var layout = ReadResource("_layout");
        var fragment = ReadResource(templateKey);

        var title = TitleRegex().Match(fragment);
        if (!title.Success)
        {
            throw new InvalidOperationException($"La plantilla '{templateKey}' no tiene asunto.");
        }

        var body = fragment.Remove(title.Index, title.Length).Trim();

        return new NotificationTemplate(
            title.Groups[1].Value.Trim(),
            layout.Replace("{Content}", body, StringComparison.Ordinal));
    }

    private string ReadResource(string name)
    {
        using var stream = _assembly.GetManifestResourceStream($"{_resourcePrefix}{name}.html")
            ?? throw new InvalidOperationException($"No existe la plantilla '{name}'.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Trim();
    }

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.Singleline)]
    private static partial Regex TitleRegex();
}