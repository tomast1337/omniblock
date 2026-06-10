namespace BetaSharp.Client.Options;

public class ShaderConstOption : GameOption
{
    private readonly ShaderOptionSet _set;
    private readonly ShaderOptionSet.OptionDef _def;

    public ShaderConstOption(KeyValuePair<string, ShaderOptionSet> set, ShaderOptionSet.OptionDef def)
        : base($"options.shader.{set.Key}.{def.Name}", $"cloudShader_{def.Name}")
    {
        _set = set.Value;
        _def = def;
    }

    public void Cycle(int direction = 1)
    {
        int len = _def.AllowedValues.Length;
        int next = ((_set.GetIndex(_def.Name) + direction) % len + len) % len;
        _set.SetIndex(_def.Name, next);
    }

    public override void Reset() => _set.SetIndex(_def.Name, _def.DefaultIndex);

    public override string FormatValue() =>
        _def.AllowedValues[_set.GetIndex(_def.Name)];

    public override void Load(string raw) { }
    public override string Save() => string.Empty;
}
