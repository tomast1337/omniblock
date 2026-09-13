using System.Text.Json;

namespace OmniBlock.Tests.Translations;

/// <summary>
///     Keeps the shipped locale catalogs aligned with US English, which is also the runtime
///     fallback. Extra locale-specific entries are allowed so a translation can prepare for a
///     feature before the English wording lands.
/// </summary>
public sealed class TranslationCompletenessTests
{
    private static readonly string s_languageDirectory =
        Path.Combine(AppContext.BaseDirectory, "assets", "lang");

    [Fact]
    public void Every_registered_language_has_every_English_translation_key()
    {
        var manifestPath = Path.Combine(s_languageDirectory, "lang.json");
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var registeredLanguages = manifestDocument.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var english = ReadLeafKeys(Path.Combine(s_languageDirectory, "en_us.json"));
        List<string> failures = [];

        foreach (var language in registeredLanguages)
        {
            var path = Path.Combine(s_languageDirectory, language + ".json");
            if (!File.Exists(path))
            {
                failures.Add($"{language}: catalog file is missing");
                continue;
            }

            var translated = ReadLeafKeys(path);
            var missing = english.Keys.Except(translated.Keys, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                failures.Add($"{language}: missing {missing.Length} key(s): {string.Join(", ", missing)}");
        }

        var unregistered = Directory.EnumerateFiles(s_languageDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != "lang" && !registeredLanguages.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unregistered.Length > 0)
            failures.Add("unregistered catalog file(s): " + string.Join(", ", unregistered));

        Assert.True(failures.Count == 0,
            "Translation catalogs must contain every en_us key.\n" + string.Join("\n", failures));
    }

    private static IReadOnlyDictionary<string, string> ReadLeafKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        Dictionary<string, string> keys = new(StringComparer.Ordinal);
        Flatten(document.RootElement, path, keys, "");
        return keys;
    }

    private static void Flatten(
        JsonElement element,
        string path,
        Dictionary<string, string> keys,
        string prefix)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
                Flatten(property.Value, path, keys, key);
            }

            return;
        }

        Assert.True(element.ValueKind is JsonValueKind.String or JsonValueKind.Number
                or JsonValueKind.True or JsonValueKind.False,
            $"{path}: translation '{prefix}' must be a scalar value");
        Assert.True(keys.TryAdd(prefix, element.ToString()), $"{path}: duplicate translation key '{prefix}'");
    }
}
