using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace BetaSharp.Registries.Data;

/// <summary>
/// A load-once, process-global map of canonical runtime instances built from data assets.
/// <para>
/// Every key resolves to exactly one instance for the lifetime of the process, so callers may
/// safely compare entries by reference. <see cref="Initialize"/> is idempotent: once loaded,
/// later calls are ignored — replacing canonical instances would silently break every
/// reference-equality comparison already handed out.
/// </para>
/// </summary>
internal sealed class CanonicalRegistry<T>(string registryName) where T : class
{
    private FrozenDictionary<string, T>? _entries;

    public bool IsInitialized => _entries != null;

    public void Initialize<TDefinition>(IEnumerable<TDefinition> definitions, Func<TDefinition, T> convert)
        where TDefinition : class, IDataAsset
    {
        if (_entries != null) return;

        var entries = new Dictionary<string, T>();
        foreach (TDefinition definition in definitions)
        {
            entries[definition.Name] = convert(definition);
        }

        _entries = entries.ToFrozenDictionary();
    }

    public T Get(string key)
    {
        if (_entries == null) throw NotInitialized();
        return _entries.TryGetValue(key, out T? value)
            ? value
            : throw new KeyNotFoundException($"Unknown {registryName} key '{key}'.");
    }

    public bool TryGet(string key, [NotNullWhen(true)] out T? value)
    {
        if (_entries == null) throw NotInitialized();
        return _entries.TryGetValue(key, out value);
    }

    private InvalidOperationException NotInitialized() => new(
        $"The {registryName} registry has not been initialized. " +
        $"Call Bootstrap.Initialize() before accessing {registryName} data (e.g. before touching Block).");
}
