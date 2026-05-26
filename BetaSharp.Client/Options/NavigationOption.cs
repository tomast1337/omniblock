namespace BetaSharp.Client.Options;

public class NavigationOption : GameOption
{
    private readonly Action _navigate;

    public NavigationOption(string label, Action navigate) : base(label, string.Empty)
    {
        _navigate = navigate;
    }

    public void Execute() => _navigate();
    public override string GetDisplayString(TranslationStorage translations) => translations.TranslateKey(TranslationKey);
    public override string FormatValue(TranslationStorage _) => string.Empty;
    public override void Load(string raw) { }
    public override string Save() => string.Empty;
}
