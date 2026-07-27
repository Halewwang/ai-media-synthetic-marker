namespace Emke.AiMarker.App.Services;

public interface IAppText
{
    string Get(string key);

    string Format(string key, params object[] arguments);
}
