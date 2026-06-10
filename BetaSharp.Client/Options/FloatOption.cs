using System.Globalization;

namespace BetaSharp.Client.Options;

public class FloatOption : GameOption
{
    public float Value { get; set; }
    public float DefaultValue { get; }
    public Func<float, string>? Formatter { get; init; }
    public Action<float>? OnChanged { get; init; }
    public int? Steps { get; init; }

    public FloatOption(string translationKey, string saveKey, float defaultValue = 0f) : base(translationKey, saveKey)
    {
        Value = defaultValue;
        DefaultValue = defaultValue;
    }

    public override void Reset() => Set(DefaultValue);

    public void Set(float value)
    {
        Value = Math.Clamp(value, 0f, 1f);

        if (Steps.HasValue && Steps.Value > 0)
        {
            Value = MathF.Round(Value * Steps.Value) / Steps.Value;
        }

        OnChanged?.Invoke(Value);
    }

    public override string FormatValue()
    {
        if (Formatter != null)
        {
            return Formatter(Value);
        }

        return Value == 0.0F
            ? Translations.Get("options.off")
            : $"{(int)(Value * 100.0F)}%";
    }

    public override void Load(string raw)
    {
        Value = raw switch
        {
            "true" => 1.0F,
            "false" => 0.0F,
            _ => float.Parse(raw, CultureInfo.InvariantCulture)
        };
    }

    public override string Save() => Value.ToString(CultureInfo.InvariantCulture);
}
