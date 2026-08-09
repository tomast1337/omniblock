using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hexa.NET.ImGui.Backends.Vulkan;

namespace BetaSharp;

public class Translations
{
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

    public SortedDictionary<string, Language> Languages { get; private set; } = new SortedDictionary<string, Language>();
    public Language? CurrentLanguage { get; private set; }
    public Language? DefaultLanguage { get; private set; }
    private Translations() { }

    public static event Action? LanguageChanged;

    private void LoadLanguages()
    {
        var asset = AssetManager.Instance.GetAsset("lang/lang.json");
        var json = JsonDocument.Parse(asset.GetTextContent());

        var element = json.RootElement;
        foreach (var item in element.EnumerateObject())
        {
            string code = item.Name;
            var value = item.Value;

            // A JSON null for either reads back as null. Neither is worth refusing to start over:
            // the code itself names the language well enough, and the credit is decoration.
            string name = value.GetProperty("name").GetString() ?? code;
            string author = value.GetProperty("author").GetString() ?? string.Empty;

            Languages.Add(code, new Language(code, name, author));

            if (value.TryGetProperty("unifont", out JsonElement propertyValue))
            {
                Languages[code].Unifont = propertyValue.GetBoolean();
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

    public static string Get(string key) => Instance[key];

    public static string GetFormat(string key, params object[] values)
    {
        string str = Get(key);

        for (int i = 0; i < values.Length; i++)
        {
            str = str.Replace($"%{i + 1}$s", values[i]?.ToString() ?? string.Empty);
        }

        return str;
    }

    public static string GetNamed(string key)
    {
        return Get($"{key}.name");
    }

    public static void SwitchLanguage(string lang)
    {
        if (!Instance.Languages.TryGetValue(lang, out Language? language)) return;

        Instance.CurrentLanguage = language;
        LanguageChanged?.Invoke();
    }
}
