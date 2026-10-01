using System.Collections.Frozen;

namespace OmniBlock.Registries;

/// <summary>
/// Named, build-time views of a block's 16 legacy metadata states. Render resources can select
/// these properties without changing the protocol/save metadata or owning its interpretation.
/// </summary>
public interface IBlockStatePropertyView
{
    bool Supports(ResourceLocation block, string property);
    bool Accepts(ResourceLocation block, string property, string value);
    string? Get(ResourceLocation block, int metadata, string property);
}

public sealed class RuntimeBlockStatePropertyRegistry : IBlockStatePropertyView
{
    private readonly FrozenDictionary<ResourceLocation, FrozenDictionary<string, string?[]>> _blocks;

    internal RuntimeBlockStatePropertyRegistry(
        IReadOnlyDictionary<ResourceLocation, Dictionary<string, string?[]>> definitions,
        IEnumerable<ResourceLocation> registeredBlocks)
    {
        var keys = registeredBlocks.ToHashSet();
        var blocks = new Dictionary<ResourceLocation, FrozenDictionary<string, string?[]>>();
        foreach (var (block, properties) in definitions)
        {
            if (!keys.Contains(block))
                throw new InvalidOperationException(
                    $"Block-state properties for '{block}': owning block is not registered.");
            blocks.Add(block, properties.ToDictionary(
                static pair => pair.Key, static pair => (string?[])pair.Value.Clone(),
                StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal));
        }
        _blocks = blocks.ToFrozenDictionary();
    }

    public bool Supports(ResourceLocation block, string property) =>
        _blocks.TryGetValue(block, out var properties) && properties.ContainsKey(property);

    public bool Accepts(ResourceLocation block, string property, string value) =>
        _blocks.TryGetValue(block, out var properties) &&
        properties.TryGetValue(property, out var states) &&
        states.Contains(value, StringComparer.Ordinal);

    public string? Get(ResourceLocation block, int metadata, string property) =>
        metadata is >= 0 and < 16 &&
        _blocks.TryGetValue(block, out var properties) &&
        properties.TryGetValue(property, out var states)
            ? states[metadata]
            : null;
}

internal sealed class BlockStatePropertyRegistryBuilder
{
    private readonly Dictionary<ResourceLocation, Dictionary<string, string?[]>> _definitions = [];

    internal void Register(ResourceLocation block, string property, IReadOnlyList<string?> states)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(states);
        if (!ValidToken(property))
            throw new ArgumentException($"Block '{block}': invalid state property '{property}'.", nameof(property));
        if (states.Count != 16)
            throw new ArgumentException($"Block '{block}' property '{property}': expected exactly 16 metadata values.", nameof(states));
        var copy = states.ToArray();
        if (copy.All(static value => value is null))
            throw new ArgumentException($"Block '{block}' property '{property}': no metadata state has a value.", nameof(states));
        foreach (var value in copy)
            if (value is not null && !ValidToken(value))
                throw new ArgumentException($"Block '{block}' property '{property}': invalid value '{value}'.", nameof(states));
        if (!_definitions.TryGetValue(block, out var properties))
            _definitions.Add(block, properties = new Dictionary<string, string?[]>(StringComparer.Ordinal));
        if (!properties.TryAdd(property, copy))
            throw new InvalidOperationException($"Block '{block}': duplicate state property '{property}'.");
    }

    internal RuntimeBlockStatePropertyRegistry Build(IEnumerable<ResourceLocation> registeredBlocks) =>
        new(_definitions, registeredBlocks);

    private static bool ValidToken(string value) => value.Length is > 0 and <= 128 &&
        value.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or ':');
}
