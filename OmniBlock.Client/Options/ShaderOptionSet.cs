using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniBlock.Client.Options;

public class ShaderOptionSet
{
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

    private readonly Dictionary<string, float> _floatValues = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
    private readonly List<PresetDef> _presetDefs = [];

    public IReadOnlyList<OptionDef> Options { get; private set; } = [];
    public IReadOnlyList<PresetDef> Presets => _presetDefs;
    public event Action<ShaderOptionSet>? Changed;

    public void Parse(string source)
    {
        List<OptionDef> defs = [];
        foreach (Match m in s_optionPattern.Matches(source))
        {
            var type = m.Groups[1].Value;
            var name = m.Groups[2].Value;
            var currentValue = m.Groups[3].Value.Trim();
            var bracket = m.Groups[4].Value.Trim();

            var rangeMatch = s_rangePattern.Match(bracket);
            if (rangeMatch.Success)
            {
                var minStr = rangeMatch.Groups[1].Value;
                var maxStr = rangeMatch.Groups[2].Value;
                var min = float.Parse(minStr, CultureInfo.InvariantCulture);
                var max = float.Parse(maxStr, CultureInfo.InvariantCulture);
                var defaultVal = float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : (min + max) / 2f;

                var decimalPlaces = type == "int"
                    ? 0
                    : Math.Max(CountDecimalPlaces(minStr), CountDecimalPlaces(maxStr));

                if (_floatValues.TryGetValue(name, out var loaded))
                    _floatValues[name] = Math.Clamp(loaded, min, max);
                else
                    _floatValues[name] = defaultVal;

                defs.Add(new OptionDef(name, type, [], 0, true, min, max, decimalPlaces, defaultVal));
            }
            else
            {
                var allowed = bracket.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var defaultIndex = Array.IndexOf(allowed, currentValue);
                if (defaultIndex < 0) defaultIndex = 0;

                if (_indices.TryGetValue(name, out var loaded))
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
            var presetName = m.Groups[1].Value;
            var parent = m.Groups[2].Success ? m.Groups[2].Value : null;
            Dictionary<string, string> vals = new(StringComparer.Ordinal);
            foreach (Match kv in s_presetKvPattern.Matches(m.Groups[3].Value))
                vals[kv.Groups[1].Value] = kv.Groups[2].Value;
            _presetDefs.Add(new PresetDef(presetName, parent, vals));
        }
    }

    public int GetIndex(string name) =>
        _indices.TryGetValue(name, out var i) ? i : 0;

    public void SetIndex(string name, int index)
    {
        if (_indices.TryGetValue(name, out var cur) && cur == index) return;
        _indices[name] = index;
        Changed?.Invoke(this);
    }

    public float GetFloat(string name, float defaultValue = 0f) =>
        _floatValues.TryGetValue(name, out var v) ? v : defaultValue;

    public void SetFloat(string name, float value)
    {
        if (_floatValues.TryGetValue(name, out var cur) && MathF.Abs(cur - value) < 1e-6f) return;
        _floatValues[name] = value;
        Changed?.Invoke(this);
    }

    public string Inject(string source)
    {
        foreach (var opt in Options)
        {
            var value = opt.IsRange
                ? FormatShaderValue(_floatValues.TryGetValue(opt.Name, out var f) ? f : (opt.RangeMin + opt.RangeMax) / 2f, opt.GlslType, opt.DecimalPlaces)
                : opt.AllowedValues[GetIndex(opt.Name)];
            source = ReplaceConstValue(source, opt.Name, value);
        }

        return source;
    }

    public IEnumerable<(string Key, string Value)> Save()
    {
        foreach (var opt in Options)
        {
            if (opt.IsRange)
            {
                var v = _floatValues.TryGetValue(opt.Name, out var f) ? f : (opt.RangeMin + opt.RangeMax) / 2f;
                var saved = v.ToString(CultureInfo.InvariantCulture);
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
        var preset = _presetDefs.Find(p => p.Name == name);
        if (preset is null) return new Dictionary<string, string>(StringComparer.Ordinal);
        if (preset.Parent is null) return preset.RawValues;

        Dictionary<string, string> merged = new(ResolvePreset(preset.Parent), StringComparer.Ordinal);
        foreach (var (k, v) in preset.RawValues)
            merged[k] = v;
        return merged;
    }

    public void ApplyPreset(string name)
    {
        var values = ResolvePreset(name);
        foreach (var opt in Options)
        {
            if (!values.TryGetValue(opt.Name, out var raw)) continue;
            if (opt.IsRange)
            {
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    SetFloat(opt.Name, Math.Clamp(f, opt.RangeMin, opt.RangeMax));
            }
            else
            {
                var idx = Array.IndexOf(opt.AllowedValues, raw);
                if (idx >= 0) SetIndex(opt.Name, idx);
            }
        }
    }

    public string GetCurrentPresetName()
    {
        foreach (var preset in _presetDefs)
        {
            var resolved = ResolvePreset(preset.Name);
            var match = true;
            foreach (var opt in Options)
            {
                if (!resolved.TryGetValue(opt.Name, out var raw)) continue;
                if (opt.IsRange)
                {
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var presetVal))
                    {
                        match = false;
                        break;
                    }

                    var current = _floatValues.TryGetValue(opt.Name, out var f) ? f : opt.DefaultFloat;
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
                    var presetIdx = Array.IndexOf(opt.AllowedValues, raw);
                    var currentIdx = _indices.TryGetValue(opt.Name, out var i) ? i : opt.DefaultIndex;
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
        if (rawValue.Contains('.') && float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            _floatValues[name] = f;
        else if (int.TryParse(rawValue, out var idx))
            _indices[name] = idx;
    }

    internal static string FormatShaderValue(float v, string glslType, int decimalPlaces)
    {
        if (glslType == "int")
            return ((int)MathF.Round(v)).ToString();

        if (decimalPlaces <= 0)
        {
            var s = v.ToString("G6", CultureInfo.InvariantCulture);
            return s.Contains('.') ? s : s + ".0";
        }

        return v.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);
    }

    internal static string PrettifyName(string name)
    {
        StringBuilder sb = new();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(sb.Length == 0 ? char.ToUpper(c) : c);
        }

        return sb.ToString();
    }

    private static int CountDecimalPlaces(string numberStr)
    {
        var dot = numberStr.IndexOf('.');
        return dot < 0 ? 0 : numberStr.Length - dot - 1;
    }

    private static string ReplaceConstValue(string source, string name, string newValue)
    {
        var marker = $" {name} = ";
        var idx = source.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return source;
        var valueStart = idx + marker.Length;
        var valueEnd = source.IndexOf(';', valueStart);
        if (valueEnd < 0) return source;
        return string.Concat(source.AsSpan(0, valueStart), newValue, source.AsSpan(valueEnd));
    }

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
}
