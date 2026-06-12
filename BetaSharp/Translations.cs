using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hexa.NET.ImGui.Backends.Vulkan;

namespace BetaSharp;

public class Translations
{
    public static Translations? Instance { get; private set; }

    public Dictionary<string, Language> Languages { get; private set; } = new Dictionary<string, Language>();
    public Language? CurrentLanguage { get; private set; }
    public Language? DefaultLanguage { get; private set; }
    private Translations() { }

    public static event Action? LanguageChanged;

    private void LoadLanguages()
    {
        var asset = AssetManager.Instance.getAsset("lang/lang.json");
        var json = JsonDocument.Parse(asset.GetTextContent());

        var element = json.RootElement;
        foreach (var item in element.EnumerateObject())
        {
            var code = item.Name;
            var value = item.Value;

            var name = value.GetProperty("name").GetString();
            var author = value.GetProperty("author").GetString();

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
                if (DefaultLanguage is null) return key;
                return DefaultLanguage.Get(key);
            }

            return CurrentLanguage.Get(key);
        }
    }

    public static string Get(string key)
    {
        if (Instance is null) return key;

        return Instance[key];
    }

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
        return Get(key + ".name");
    }

    public static void SwitchLanguage(string lang)
    {
        if (!Instance.Languages.ContainsKey(lang)) return;

        Instance.CurrentLanguage = Instance.Languages[lang];
        LanguageChanged?.Invoke();
    }
}
