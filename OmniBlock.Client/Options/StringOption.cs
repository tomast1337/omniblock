namespace OmniBlock.Client.Options;

public class StringOption : GameOption
{
    public StringOption(string translationKey, string saveKey, string defaultValue = "") : base(translationKey, saveKey) => Value = defaultValue;
    public string Value { get; set; }
    public Action<string>? OnChanged { get; init; }

    public override string FormatValue() => Value;

    public override void Load(string raw) => Value = raw;

    public override string Save() => Value.ToLower();
}
