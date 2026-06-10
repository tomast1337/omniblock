namespace BetaSharp.Client.Options;

public class CycleOption : GameOption
{
    public int Value { get; set; }
    public int DefaultIndex { get; }
    public int Length { get; }
    public string[] Labels { get; }
    public Func<int, string>? Formatter { get; init; }
    public Action<int>? OnChanged { get; init; }

    public CycleOption(string translationKey, string saveKey, string[] labels, int defaultValue = 0) : this(translationKey, saveKey, labels, defaultValue, labels.Length) { }

    public CycleOption(string translationKey, string saveKey, string[] labels, int defaultValue, int length) : base(translationKey, saveKey)
    {
        Labels = labels;
        Value = defaultValue;
        DefaultIndex = defaultValue;
        Length = length;
    }

    public void Cycle(int increment = 1)
    {
        Value = (Value + increment) % Length;
        OnChanged?.Invoke(Value);
    }

    public override void Reset()
    {
        Value = DefaultIndex;
        OnChanged?.Invoke(Value);
    }

    public override string FormatValue()
    {
        if (Formatter != null)
        {
            return Formatter(Value);
        }

        if (Labels.Length <= Length)
            return Translations.Get(Labels[Value]);
        return Labels.Length <= Value ? Translations.Get(Labels[Value]) : Translations.Get(Labels.Last());
    }

    public override void Load(string raw)
    {
        Value = int.Parse(raw) % Length;
    }

    public override string Save() => Value.ToString();
}
