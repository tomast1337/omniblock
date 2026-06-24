namespace BetaSharp.Client.Options;

public class ShaderPresetOption : GameOption
{
    private readonly ShaderOptionSet _set;

    public ShaderPresetOption(string shaderName, ShaderOptionSet set)
        : base($"options.shader.preset.text", string.Empty)
    {
        _set = set;
    }

    public void Cycle(int direction = 1)
    {
        IReadOnlyList<ShaderOptionSet.PresetDef> presets = _set.Presets;
        if (presets.Count == 0) return;

        string current = _set.GetCurrentPresetName();
        int currentIdx = -1;
        for (int i = 0; i < presets.Count; i++)
        {
            if (presets[i].Name == current)
            {
                currentIdx = i;
                break;
            }
        }

        int next = currentIdx < 0
            ? (direction > 0 ? 0 : presets.Count - 1)
            : ((currentIdx + direction) % presets.Count + presets.Count) % presets.Count;

        _set.ApplyPreset(presets[next].Name);
    }

    public override string FormatValue() =>
        Translations.Get("options.shader.preset." + _set.GetCurrentPresetName());

    public override void Reset() { }
    public override void Load(string raw) { }
    public override string Save() => string.Empty;
}
