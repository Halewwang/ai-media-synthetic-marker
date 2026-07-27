using System.Globalization;
using System.Windows;

namespace Emke.AiMarker.App.Services;

public sealed class ResourceAppText : IAppText
{
    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Application.Current.TryFindResource(key) as string
            ?? throw new KeyNotFoundException($"Missing application text resource: {key}");
    }

    public string Format(string key, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
    }
}
