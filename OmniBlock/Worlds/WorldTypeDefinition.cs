using OmniBlock.Registries.Data;
using System.Text.Json;

namespace OmniBlock.Worlds;

/// <summary>
///     Data-owned world preset. Generator options are intentionally absent: their schema belongs to
///     the namespaced generator provider selected by <see cref="Generator" />.
/// </summary>
public sealed class WorldTypeDefinition : DataAsset
{
    public string Generator { get; init; } = "";
    public JsonElement GeneratorSettings { get; init; }
    public string IconPath { get; init; } = "";
    public bool CanBeCreated { get; init; } = true;
}
