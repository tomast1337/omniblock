namespace BetaSharp.Client.Options;

public class ShaderOptionsRegistry
{
    private readonly Dictionary<string, ShaderOptionSet> _sets = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ShaderOptionSet> Sets => _sets;

    public ShaderOptionSet GetOrCreate(string name)
    {
        if (!_sets.TryGetValue(name, out ShaderOptionSet? set))
            _sets[name] = set = new ShaderOptionSet();
        return set;
    }

    public void Load(string shaderName, string optName, string rawIndex)
        => GetOrCreate(shaderName).Load(optName, rawIndex);

    public IEnumerable<(string Key, string Value)> Save()
    {
        foreach ((string name, ShaderOptionSet set) in _sets)
            foreach ((string optKey, string val) in set.Save())
                yield return ($"shaderOpt_{name}.{optKey}", val);
    }
}
