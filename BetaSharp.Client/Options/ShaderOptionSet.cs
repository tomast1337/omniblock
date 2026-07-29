using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BetaSharp.Client.Options;

public class ShaderOptionSet
{
    public record OptionDef(
        string Name,
        string GlslType,
        string[] AllowedValues,
        int DefaultIndex,
        bool IsRange = false,
        float RangeMin = 0f,
        float RangeMax = 1f,
        int DecimalPlaces = 2,
        float DefaultFloat = 0f);

    public record PresetDef(string Name, string? Parent, IReadOnlyDictionary<string, string> RawValues);

    private static readonly Regex s_optionPattern = new(
        @"^\s*const\s+(int|float)\s+(\w+)\s*=\s*([^;]+);\s*//\s*\[([^\]]+)\]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_rangePattern = new(
        @"^(-?\d+\.?\d*)\s*-\s*(-?\d+\.?\d*)$",
        RegexOptions.Compiled);

    private static readonly Regex s_presetPattern = new(
        @"^\s*//\s*\[preset:(\w+)(?::(\w+))?\](.*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_presetKvPattern = new(
        @"(\w+)=([^\s]+)",
        RegexOptions.Compiled);

    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _floatValues = new(StringComparer.Ordinal);
    private readonly List<PresetDef> _presetDefs = [];

    public IReadOnlyList<OptionDef> Options { get; private set; } = [];
    public IReadOnlyList<PresetDef> Presets => _presetDefs;
    public event Action<ShaderOptionSet>? Changed;

    public void Parse(string source)
    {
        List<OptionDef> defs = [];
        foreach (Match m in s_optionPattern.Matches(source))
        {
            string type = m.Groups[1].Value;
            string name = m.Groups[2].Value;
            string currentValue = m.Groups[3].Value.Trim();
            string bracket = m.Groups[4].Value.Trim();

            Match rangeMatch = s_rangePattern.Match(bracket);
            if (rangeMatch.Success)
            {
                string minStr = rangeMatch.Groups[1].Value;
                string maxStr = rangeMatch.Groups[2].Value;
                float min = float.Parse(minStr, CultureInfo.InvariantCulture);
                float max = float.Parse(maxStr, CultureInfo.InvariantCulture);
                float defaultVal = float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    ? parsed
                    : (min + max) / 2f;

                int decimalPlaces = type == "int"
                    ? 0
                    : Math.Max(CountDecimalPlaces(minStr), CountDecimalPlaces(maxStr));

                if (_floatValues.TryGetValue(name, out float loaded))
                    _floatValues[name] = Math.Clamp(loaded, min, max);
                else
                    _floatValues[name] = defaultVal;

                defs.Add(new OptionDef(name, type, [], 0, IsRange: true, RangeMin: min, RangeMax: max, DecimalPlaces: decimalPlaces, DefaultFloat: defaultVal));
            }
            else
            {
                string[] allowed = bracket.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int defaultIndex = Array.IndexOf(allowed, currentValue);
                if (defaultIndex < 0) defaultIndex = 0;

                if (_indices.TryGetValue(name, out int loaded))
                    _indices[name] = Math.Clamp(loaded, 0, allowed.Length - 1);
                else
                    _indices[name] = defaultIndex;

                defs.Add(new OptionDef(name, type, allowed, defaultIndex));
            }
        }

        Options = defs;

        _presetDefs.Clear();
        foreach (Match m in s_presetPattern.Matches(source))
        {
            string presetName = m.Groups[1].Value;
            string? parent = m.Groups[2].Success ? m.Groups[2].Value : null;
            Dictionary<string, string> vals = new(StringComparer.Ordinal);
            foreach (Match kv in s_presetKvPattern.Matches(m.Groups[3].Value))
                vals[kv.Groups[1].Value] = kv.Groups[2].Value;
            _presetDefs.Add(new PresetDef(presetName, parent, vals));
        }
    }

    public int GetIndex(string name) =>
        _indices.TryGetValue(name, out int i) ? i : 0;

    public void SetIndex(string name, int index)
    {
        if (_indices.TryGetValue(name, out int cur) && cur == index) return;
        _indices[name] = index;
        Changed?.Invoke(this);
    }

    public float GetFloat(string name, float defaultValue = 0f) =>
        _floatValues.TryGetValue(name, out float v) ? v : defaultValue;

    public void SetFloat(string name, float value)
    {
        if (_floatValues.TryGetValue(name, out float cur) && MathF.Abs(cur - value) < 1e-6f) return;
        _floatValues[name] = value;
        Changed?.Invoke(this);
    }

    public string Inject(string source)
    {
        foreach (OptionDef opt in Options)
        {
            string value = opt.IsRange
                ? FormatShaderValue(_floatValues.TryGetValue(opt.Name, out float f) ? f : (opt.RangeMin + opt.RangeMax) / 2f, opt.GlslType, opt.DecimalPlaces)
                : opt.AllowedValues[GetIndex(opt.Name)];
            source = ReplaceConstValue(source, opt.Name, value);
        }

        return source;
    }

    public IEnumerable<(string Key, string Value)> Save()
    {
        foreach (OptionDef opt in Options)
        {
            if (opt.IsRange)
            {
                float v = _floatValues.TryGetValue(opt.Name, out float f) ? f : (opt.RangeMin + opt.RangeMax) / 2f;
                string saved = v.ToString(CultureInfo.InvariantCulture);
                if (!saved.Contains('.')) saved += ".0";
                yield return (opt.Name, saved);
            }
            else
            {
                yield return (opt.Name, GetIndex(opt.Name).ToString());
            }
        }
    }

    public IReadOnlyDictionary<string, string> ResolvePreset(string name)
    {
        PresetDef? preset = _presetDefs.Find(p => p.Name == name);
        if (preset is null) return new Dictionary<string, string>(StringComparer.Ordinal);
        if (preset.Parent is null) return preset.RawValues;

        Dictionary<string, string> merged = new(ResolvePreset(preset.Parent), StringComparer.Ordinal);
        foreach ((string k, string v) in preset.RawValues)
            merged[k] = v;
        return merged;
    }

    public void ApplyPreset(string name)
    {
        IReadOnlyDictionary<string, string> values = ResolvePreset(name);
        foreach (OptionDef opt in Options)
        {
            if (!values.TryGetValue(opt.Name, out string? raw)) continue;
            if (opt.IsRange)
            {
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    SetFloat(opt.Name, Math.Clamp(f, opt.RangeMin, opt.RangeMax));
            }
            else
            {
                int idx = Array.IndexOf(opt.AllowedValues, raw);
                if (idx >= 0) SetIndex(opt.Name, idx);
            }
        }
    }

    public string GetCurrentPresetName()
    {
        foreach (PresetDef preset in _presetDefs)
        {
            IReadOnlyDictionary<string, string> resolved = ResolvePreset(preset.Name);
            bool match = true;
            foreach (OptionDef opt in Options)
            {
                if (!resolved.TryGetValue(opt.Name, out string? raw)) continue;
                if (opt.IsRange)
                {
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float presetVal))
                    {
                        match = false;
                        break;
                    }

                    float current = _floatValues.TryGetValue(opt.Name, out float f) ? f : opt.DefaultFloat;
                    if (opt.GlslType == "int")
                    {
                        presetVal = MathF.Round(presetVal);
                        current = MathF.Round(current);
                    }

                    if (MathF.Abs(current - presetVal) > 1e-4f)
                    {
                        match = false;
                        break;
                    }
                }
                else
                {
                    int presetIdx = Array.IndexOf(opt.AllowedValues, raw);
                    int currentIdx = _indices.TryGetValue(opt.Name, out int i) ? i : opt.DefaultIndex;
                    if (presetIdx != currentIdx)
                    {
                        match = false;
                        break;
                    }
                }
            }

            if (match) return preset.Name;
        }

        return "Custom";
    }

    public void Load(string name, string rawValue)
    {
        if (rawValue.Contains('.') && float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            _floatValues[name] = f;
        else if (int.TryParse(rawValue, out int idx))
            _indices[name] = idx;
    }

    internal static string FormatShaderValue(float v, string glslType, int decimalPlaces)
    {
        if (glslType == "int")
            return ((int)MathF.Round(v)).ToString();

        if (decimalPlaces <= 0)
        {
            string s = v.ToString("G6", CultureInfo.InvariantCulture);
            return s.Contains('.') ? s : s + ".0";
        }

        return v.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);
    }

    internal static string PrettifyName(string name)
    {
        StringBuilder sb = new();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(sb.Length == 0 ? char.ToUpper(c) : c);
        }

        return sb.ToString();
    }

    private static int CountDecimalPlaces(string numberStr)
    {
        int dot = numberStr.IndexOf('.');
        return dot < 0 ? 0 : numberStr.Length - dot - 1;
    }

    private static string ReplaceConstValue(string source, string name, string newValue)
    {
        string marker = $" {name} = ";
        int idx = source.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return source;
        int valueStart = idx + marker.Length;
        int valueEnd = source.IndexOf(';', valueStart);
        if (valueEnd < 0) return source;
        return string.Concat(source.AsSpan(0, valueStart), newValue, source.AsSpan(valueEnd));
    }
}
