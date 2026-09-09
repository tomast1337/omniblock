using System.Text.Json;

namespace OmniBlock;

public class Translations
{
    private Translations()
    {
    }

    /// <summary>
    ///     The loaded translations, or an empty set before <see cref="Init" /> has run.
    /// </summary>
    /// <remarks>
    ///     Never null. An instance that knows no languages already behaves the way callers wanted
    ///     the null to behave: <see cref="this[string]" /> hands back the key it was given, and
    ///     <see cref="SwitchLanguage" /> finds nothing to switch to. Leaving it null instead made
    ///     that every caller's problem, and <c>SwitchLanguage</c> was one of the callers that
    ///     forgot.
    /// </remarks>
    public static Translations Instance { get; private set; } = new();

    public SortedDictionary<string, Language> Languages { get; } = new();
    public Language? CurrentLanguage { get; private set; }
    public Language? DefaultLanguage { get; private set; }

    public string this[string key]
    {
        get
        {
            if (CurrentLanguage is null)
            {
                return DefaultLanguage is null ? key : DefaultLanguage.Get(key);
            }

            return CurrentLanguage.Get(key);
        }
    }

    public static event Action? LanguageChanged;

    private void LoadLanguages()
    {
        var asset = AssetManager.Instance.GetAsset("lang/lang.json");
        var json = JsonDocument.Parse(asset.GetTextContent());

        var element = json.RootElement;
        foreach (var item in element.EnumerateObject())
        {
            var code = item.Name;
            var value = item.Value;

            // A JSON null for either reads back as null. Neither is worth refusing to start over:
            // the code itself names the language well enough, and the credit is decoration.
            var name = value.GetProperty("name").GetString() ?? code;
            var author = value.GetProperty("author").GetString() ?? string.Empty;

            Languages.Add(code, new Language(code, name, author));

            if (value.TryGetProperty("unifont", out var propertyValue))
            {
                Languages[code].Unifont = propertyValue.GetBoolean();
            }

            if (value.TryGetProperty("sevenish", out var sevenishValue))
            {
                Languages[code].Sevenish = sevenishValue.GetBoolean();
            }

            if (code == "en_us") DefaultLanguage = Languages[code];
        }

        CurrentLanguage = DefaultLanguage;
    }

    public static void Init()
    {
        Instance = new Translations();
        Instance.LoadLanguages();
    }

    public static string Get(string key) => Instance[key];

    public static string GetFormat(string key, params object[] values)
    {
        var str = Get(key);

        for (var i = 0; i < values.Length; i++)
        {
            str = str.Replace($"%{i + 1}$s", values[i]?.ToString() ?? string.Empty);
        }

        return str;
    }

    public static string GetNamed(string key) => Get($"{key}.name");

    /// <summary>Uses the selected language, then the default language, then the supplied fallback.</summary>
    public static string GetOrDefault(string key, string fallback)
    {
        if (Instance.CurrentLanguage?.TryGet(key, out var current) == true) return current;
        if (Instance.DefaultLanguage?.TryGet(key, out var @default) == true) return @default;
        return fallback;
    }

    public static string GetFormatOrDefault(string key, string fallback, params object[] values)
    {
        var value = GetOrDefault(key, fallback);
        for (var i = 0; i < values.Length; i++)
            value = value.Replace($"%{i + 1}$s", values[i]?.ToString() ?? string.Empty);
        return value;
    }

    public static void SwitchLanguage(string lang)
    {
        if (!Instance.Languages.TryGetValue(lang, out var language)) return;

        Instance.CurrentLanguage = language;
        LanguageChanged?.Invoke();
    }
}
