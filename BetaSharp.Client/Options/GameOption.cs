namespace BetaSharp.Client.Options;

public abstract class GameOption
{
    public string TranslationKey { get; }
    public string SaveKey { get; }
    public string? LabelOverride { get; init; }
    public bool IsSlider => this is FloatOption;

    protected GameOption(string translationKey, string saveKey)
    {
        TranslationKey = translationKey;
        SaveKey = saveKey;
    }

    public string GetLabel() =>
        LabelOverride ?? Translations.Get(TranslationKey);

    public virtual string GetDisplayString() =>
        GetLabel() + ": " + FormatValue();

    public abstract string FormatValue();
    public abstract void Load(string raw);
    public abstract string Save();
    public virtual void Reset() { }
}
