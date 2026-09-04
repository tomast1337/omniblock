namespace OmniBlock.Client.Options;

public class ShaderRangeOption : GameOption
{
    private readonly ShaderOptionSet.OptionDef _def;
    private readonly ShaderOptionSet _set;

    public ShaderRangeOption(KeyValuePair<string, ShaderOptionSet> set, ShaderOptionSet.OptionDef def)
        : base($"options.shader.{set.Key}.{def.Name}", string.Empty)
    {
        _set = set.Value;
        _def = def;
    }

    public float NormalizedValue
    {
        get
        {
            var actual = _set.GetFloat(_def.Name, (_def.RangeMin + _def.RangeMax) / 2f);
            if (_def.GlslType == "int") actual = MathF.Round(actual);
            return Math.Clamp((actual - _def.RangeMin) / (_def.RangeMax - _def.RangeMin), 0f, 1f);
        }
    }

    public void SetNormalized(float normalized)
    {
        var actual = _def.RangeMin + normalized * (_def.RangeMax - _def.RangeMin);
        actual = Math.Clamp(actual, _def.RangeMin, _def.RangeMax);
        if (_def.GlslType == "int") actual = MathF.Round(actual);
        _set.SetFloat(_def.Name, actual);
    }

    public override string FormatValue() =>
        ShaderOptionSet.FormatShaderValue(
            _set.GetFloat(_def.Name, (_def.RangeMin + _def.RangeMax) / 2f),
            _def.GlslType,
            _def.DecimalPlaces);

    public override void Reset() => _set.SetFloat(_def.Name, _def.DefaultFloat);

    public override void Load(string raw)
    {
    }

    public override string Save() => string.Empty;
}
