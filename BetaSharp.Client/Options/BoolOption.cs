namespace BetaSharp.Client.Options;

public class BoolOption : GameOption
{
    public bool Value { get; set; }
    public bool DefaultValue { get; }
    public Func<bool, string>? Formatter { get; init; }
    public Action<bool>? OnChanged { get; init; }

    public BoolOption(string translationKey, string saveKey, bool defaultValue = false) : base(translationKey, saveKey)
    {
        Value = defaultValue;
        DefaultValue = defaultValue;
    }

    public override void Reset()
    {
        Value = DefaultValue;
        OnChanged?.Invoke(Value);
    }

    public void Toggle()
    {
        Value = !Value;
        OnChanged?.Invoke(Value);
    }

    public override string FormatValue()
    {
        if (Formatter != null)
        {
            return Formatter(Value);
        }

        return Value ? Translations.Get("options.on") : Translations.Get("options.off");
    }

    public override void Load(string raw) => Value = raw == "true";

    public override string Save() => Value.ToString().ToLower();
}
