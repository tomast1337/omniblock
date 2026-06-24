namespace BetaSharp.Stats;

public static class StatCollector
{
    public static string TranslateToLocal(string key) => Translations.Get(key);

    public static string TranslateToLocalFormatted(string key, params object[] args) => Translations.GetFormat(key, args);
}
