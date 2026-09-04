namespace OmniBlock.Client.Options;

public class ShaderOptionsRegistry
{
    private readonly Dictionary<string, ShaderOptionSet> _sets = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ShaderOptionSet> Sets => _sets;

    public ShaderOptionSet GetOrCreate(string name)
    {
        if (!_sets.TryGetValue(name, out var set))
            _sets[name] = set = new ShaderOptionSet();
        return set;
    }

    public void Load(string shaderName, string optName, string rawIndex)
        => GetOrCreate(shaderName).Load(optName, rawIndex);

    public IEnumerable<(string Key, string Value)> Save()
    {
        foreach (var (name, set) in _sets)
        foreach (var (optKey, val) in set.Save())
            yield return ($"shaderOpt_{name}.{optKey}", val);
    }
}
