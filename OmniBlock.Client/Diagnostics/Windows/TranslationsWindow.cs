using System.Numerics;
using Hexa.NET.ImGui;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class TranslationsWindow : DebugWindow
{
    private readonly string[] _languageNames = Array.Empty<string>();

    private readonly Dictionary<string, Language> _nameToLanguage = new();

    private int _currentIndex = -1;
    private bool _displayMissing;
    private Language? _language;
    private string _search = string.Empty;

    public TranslationsWindow()
    {
        foreach (var item in Translations.Instance.Languages.Values)
        {
            _nameToLanguage.Add(item.Name, item);
        }

        _languageNames = _nameToLanguage.Keys.ToArray();

        if (Translations.Instance.CurrentLanguage is Language lang)
        {
            _currentIndex = Array.IndexOf(_languageNames, lang.Name);
            if (_currentIndex >= 0)
            {
                _language = _nameToLanguage[_languageNames[_currentIndex]];
            }
        }

        Translations.LanguageChanged += ChangeToCurrent;
    }

    public override string Title => "Translations";
    public override DebugDock DefaultDock => DebugDock.Right;

    private void ChangeToCurrent()
    {
        if (Translations.Instance.CurrentLanguage is Language lang)
        {
            _currentIndex = Array.IndexOf(_languageNames, lang.Name);
            _language = lang;
        }
    }

    protected override void OnDraw()
    {
        // language
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

        if (ImGui.Combo("##LanguageCombo", ref _currentIndex, _languageNames, _languageNames.Length))
        {
            var selectedName = _languageNames[_currentIndex];
            _language = _nameToLanguage[selectedName];
        }

        // set language to current
        if (ImGui.Button("Current"))
        {
            ChangeToCurrent();
        }

        // set language to default (english)
        ImGui.SameLine();
        if (ImGui.Button("Default"))
        {
            if (Translations.Instance.DefaultLanguage is Language lang)
            {
                _currentIndex = Array.IndexOf(_languageNames, lang.Name);
                _language = lang;
            }
        }

        // display missing
        ImGui.Checkbox("Display missing translations", ref _displayMissing);

        // search bar
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText("##Search", ref _search, 256);

        if (!string.IsNullOrWhiteSpace(_search))
        {
            ImGuiTextSafe.TextDisabled($"Filtering results for: '{_search}'");
        }

        ImGui.Separator();

        // strings
        if (ImGui.BeginChild("TranslationListScrollview", Vector2.Zero))
        {
            if (_language != null)
            {
                ImGuiTextSafe.TextDisabled($"Language: {_language.Name}");

                if (_language.Translations is null) _language.LoadTranslations();

                if (_language.Translations is null)
                {
                    ImGuiTextSafe.TextDisabled("Translations were not loaded, and failed to load!");

                    ImGui.EndChild();
                    return;
                }

                if (_displayMissing)
                {
                    if (Translations.Instance.DefaultLanguage is null)
                    {
                        ImGuiTextSafe.TextDisabled("Cannot show missing translations - default language is null.");
                    }
                    else
                    {
                        ImGuiTextSafe.TextDisabled("Displaying translations missing in this language:");

                        var anyMissing = false;
                        foreach (var translation in Translations.Instance.DefaultLanguage.Translations)
                        {
                            if (_language.Translations.ContainsKey(translation.Key)) continue;

                            ImGuiTextSafe.TextColored(new Vector4(0.8f, 0.4f, 0.4f, 1f), $"{translation.Key}: {translation.Value}");

                            anyMissing = true;
                        }

                        if (anyMissing) ImGui.Separator();
                    }
                }

                var noTranslations = true;

                foreach (var translation in _language.Translations)
                {
                    // filter
                    if (!string.IsNullOrWhiteSpace(_search))
                    {
                        var matchesKey = translation.Key.Contains(_search, StringComparison.OrdinalIgnoreCase);
                        var matchesValue = translation.Value?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false;

                        if (!matchesKey && !matchesValue)
                            continue;

                        noTranslations = false;
                    }

                    ImGuiTextSafe.Text($"{translation.Key}:");
                    ImGui.SameLine();
                    ImGuiTextSafe.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 1f), translation.Value ?? string.Empty);
                }

                if (noTranslations)
                {
                    ImGuiTextSafe.TextDisabled("No translation matched your filter.");
                }
            }
            else
            {
                ImGuiTextSafe.Text("Select a language to see strings.");
            }

            ImGui.EndChild();
        }
    }
}
