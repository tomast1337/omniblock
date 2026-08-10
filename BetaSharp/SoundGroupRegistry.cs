using System.Diagnostics.CodeAnalysis;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock;

/// <summary>
/// Process-global registry of canonical <see cref="BlockSoundGroup"/> instances, loaded once
/// from <c>assets/sound_group/*.json</c> during <see cref="Bootstrap.Initialize"/> — before
/// anything touches <see cref="Blocks.Block"/>, whose static fields consume sound groups.
/// </summary>
public static class SoundGroupRegistry
{
    private static readonly CanonicalRegistry<BlockSoundGroup> s_registry = new("sound group");

    /// <summary>Returns the canonical sound group for <paramref name="key"/>. Throws on unknown key.</summary>
    public static BlockSoundGroup Get(string key) => s_registry.Get(key);

    public static bool TryGet(string key, [NotNullWhen(true)] out BlockSoundGroup? group)
        => s_registry.TryGet(key, out group);

    /// <summary>Reverse lookup used by data dumpers to recover the name a sound group was loaded under.</summary>
    public static string? TryGetName(BlockSoundGroup group) => s_registry.TryGetKey(group);

    internal static void Initialize()
    {
        if (s_registry.IsInitialized) return;

        DataAssetLoader<SoundGroupDefinition> loader = (DataAssetLoader<SoundGroupDefinition>)RegistryDefinitions.SoundGroups.CreateLoader();
        loader.LoadFromPaths(null, null, null);
        if (loader.HasErrors)
        {
            throw new AssetLoadException(loader.FirstErrorMessage ?? "One or more sound group definitions failed to load.");
        }

        s_registry.Initialize(loader, CreateSoundGroup);
    }

    private static BlockSoundGroup CreateSoundGroup(SoundGroupDefinition definition)
    {
        if (definition.GroupName.Length == 0)
        {
            throw new AssetLoadException($"Sound group '{definition.Name}' is missing 'GroupName'.");
        }

        return new BlockSoundGroup(definition.GroupName, definition.Volume, definition.Pitch, definition.CustomBreakSound);
    }
}
