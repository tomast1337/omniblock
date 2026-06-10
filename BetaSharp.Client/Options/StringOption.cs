namespace BetaSharp.Client.Options;

public class StringOption : GameOption
{
    public string Value { get; set; }
    public Action<string>? OnChanged { get; init; }

    public StringOption(string translationKey, string saveKey, string defaultValue = "") : base(translationKey, saveKey)
    {
        Value = defaultValue;
    }

    public override string FormatValue()
    {
        return Value;
    }

    public override void Load(string raw) => Value = raw;

    public override string Save() => Value.ToString().ToLower();
}
