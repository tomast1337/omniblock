using System.Text;
using System.Text.Json.Nodes;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Textures;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Only invoked by the restricted gallery E2E controller, not ordinary gameplay.</summary>
internal static class BlockModelResourceSmoke
{
    public static object Run(TextureManager textures, TexturePack pack, bool mipmaps, IBlockRuntimeView blocks)
    {
        var animations = textures.PrepareAnimations(pack);
        if (animations.Count != 9) throw new InvalidOperationException("Gallery expected all nine built-in animations.");
        var textureGeneration = textures.ResourceGeneration;
        var terrainHandle = textures.GetTextureId("/terrain.png");
        var previousTerrain = terrainHandle.Texture;
        var animationFailure = false;
        try { textures.PrepareAnimations(new BrokenAnimationPack(pack)); }
        catch (InvalidDataException) { animationFailure = true; }
        if (!animationFailure || textures.ResourceGeneration != textureGeneration ||
            !ReferenceEquals(previousTerrain, terrainHandle.Texture))
            throw new InvalidOperationException("Animation preparation failure changed live resources.");
        var oldTerrainArray = textures.TerrainArray;
        var oldItemsArray = textures.ItemsArray;
        var oldColors = textures.GetColors("/misc/grasscolor.png");
        var oldModels = textures.BlockModels.Current;
        var oldBindings = textures.BlockBindings;
        var textureCount = Texture2D.ActiveTextureCount;
        var arrayCount = TextureArray.ActiveTextureCount;
        var persisted = false;
        // Fail late, after replacement 2D textures and the terrain array were uploaded.
        var badItem = "textures/items/" + Atlases.Items.Tiles[0].Name + ".png";
        if (textures.TryReload(new BrokenResourcePack(badItem), () => { persisted = true; return true; }))
            throw new InvalidOperationException("Corrupt item candidate was accepted.");
        AssertUnchanged();
        if (persisted) throw new InvalidOperationException("Invalid candidate reached option persistence.");
        // Fail the final persistence boundary after the full GPU candidate is ready.
        if (textures.TryReload(pack, () => { persisted = true; return false; }))
            throw new InvalidOperationException("Failed persistence was accepted.");
        AssertUnchanged();
        if (!persisted) throw new InvalidOperationException("Persistence failure path was not exercised.");
        const string statePath = "assets/omniblock/blockstates/slab.json";
        using var selectedSource = new TexturePackSnapshot(pack);
        using var stateStream = selectedSource.Open(statePath)
            ?? throw new InvalidOperationException("Missing shipped slab states.");
        var stateJson = JsonNode.Parse(stateStream)!;
        stateJson["variants"]!["0"]!["shape"] = "cube";
        if (textures.TryReload(new StateOverridePack(statePath, stateJson.ToJsonString(), selectedSource.OpenOverride)))
            throw new InvalidOperationException("Incompatible state shape override was accepted.");
        AssertUnchanged();
        const string fenceStatePath = FencePartDefinitions.Path;
        using var fenceStateStream = selectedSource.Open(fenceStatePath)
            ?? throw new InvalidOperationException("Missing shipped fence parts.");
        var fenceStateJson = JsonNode.Parse(fenceStateStream)!;
        fenceStateJson["parts"]![0]!["element"] = "missing_post";
        if (textures.TryReload(new StateOverridePack(fenceStatePath, fenceStateJson.ToJsonString(), selectedSource.OpenOverride)))
            throw new InvalidOperationException("Fence rule with a missing model element was accepted.");
        AssertUnchanged();
        if (textures.TryReload(new StateOverridePack(CrossedPlantModelCatalog.Path,
                "{\"overrides\":{\"omniblock:dandelion\":{\"inset\":0.5}}}", selectedSource.OpenOverride)))
            throw new InvalidOperationException("Invalid crossed-plant geometry was accepted.");
        AssertUnchanged();

        void AssertUnchanged()
        {
            if (textures.ResourceGeneration != textureGeneration || !ReferenceEquals(previousTerrain, terrainHandle.Texture) ||
                !ReferenceEquals(oldTerrainArray, textures.TerrainArray) || !ReferenceEquals(oldItemsArray, textures.ItemsArray) ||
                !ReferenceEquals(oldModels, textures.BlockModels.Current) || !ReferenceEquals(oldBindings, textures.BlockBindings) ||
                !ReferenceEquals(oldColors, textures.GetColors("/misc/grasscolor.png")) ||
                Texture2D.ActiveTextureCount != textureCount || TextureArray.ActiveTextureCount != arrayCount)
                throw new InvalidOperationException("Failed resource transaction modified live state or leaked wrappers.");
        }
        // Exercise installation of fresh animation instances through the real reload path before
        // the gallery pins its deterministic frames. This is not a full pack-switch atomicity test.
        textures.Reload();
        if (textures.ResourceGeneration != textureGeneration + 1 || ReferenceEquals(previousTerrain, terrainHandle.Texture))
            throw new InvalidOperationException("Same-pack texture/animation reload did not complete.");
        if (textures.BlockBindings?.Generation != textures.ResourceGeneration ||
            ReferenceEquals(oldBindings, textures.BlockBindings) ||
            !ReferenceEquals(textures.TerrainArray.Texture, textures.BlockModels.Current?.Texture))
            throw new InvalidOperationException("Live model bindings and terrain array are not from the committed resource candidate.");
        var samePackReloadCompleted = true;
        var slab = blocks.Get("slab").Id;
        var beforeRemap = textures.BlockBindings;
        stateJson["variants"]!["0"]!["shape"] = "lower_slab";
        stateJson["variants"]!["0"]!["model"] = "omniblock:block/wooden_slab";
        if (!textures.TryReload(new StateOverridePack(statePath, stateJson.ToJsonString(), selectedSource.OpenOverride)))
            throw new InvalidOperationException("Valid state remap failed to install.");
        var remapped = textures.BlockBindings!;
        if (remapped.Get(slab, 0)!.Face(Side.Up).ArrayLayer !=
                Atlases.Terrain.LayerOfGridIndex(Atlases.Terrain.IndexOf("wooden_planks")) ||
            remapped.Get(slab, 1)!.Face(Side.Up) != beforeRemap!.Get(slab, 1)!.Face(Side.Up))
            throw new InvalidOperationException("State remap changed the wrong variant or did not reach live bindings.");
        if (!textures.TryReload(pack) || textures.ResourceGeneration != textureGeneration + 3 ||
            textures.BlockBindings!.Get(slab, 0)!.Face(Side.Up) != beforeRemap.Get(slab, 0)!.Face(Side.Up))
            throw new InvalidOperationException("State remap did not restore before gallery capture.");

        using var sources = BlockModelPackResources.Open(pack);
        var root = RenderResourceId.Parse("omniblock:block/model_probe");
        var prepared = PreparedBlockModelResources.Build([root], sources.FixedLayers(), sources.OpenOverride, sources.OpenBuiltin);
        using var slot = new BlockModelResourceSlot();
        var generation = slot.Generation;
        slot.Replace(() => prepared.Upload(mipmaps));
        var first = slot.Current ?? throw new InvalidOperationException("First resource install failed.");
        var failed = false;
        try
        {
            slot.Replace(() => PreparedBlockModelResources.Build([root], sources.FixedLayers(),
                path => path == root.ModelPath ? new MemoryStream(Encoding.UTF8.GetBytes("{broken")) : sources.OpenOverride(path),
                sources.OpenBuiltin).Upload(mipmaps));
        }
        catch (InvalidDataException) { failed = true; }
        if (!failed || !ReferenceEquals(slot.Current, first) || slot.Generation != generation + 1 || first.Texture.Id == 0)
            throw new InvalidOperationException("Failed model reload changed published resources.");
        slot.Replace(() => prepared.Upload(mipmaps));
        var second = slot.Current ?? throw new InvalidOperationException("Second resource install failed.");
        if (first.Texture.Id != 0 || second.Texture.Id == 0 || slot.Generation != generation + 2)
            throw new InvalidOperationException("Model/texture replacement lifetime mismatch.");
        return new
        {
            status = "passed", models = prepared.Models.Count, prepared.LayerSize, prepared.LayerCount,
            generation = slot.Generation, failedReloadPreservedPrevious = failed,
            previousWrapperDisposed = first.Texture.Id == 0,
            gpuResident = second.Texture.Wgpu != null,
            preparedAnimations = animations.Count, failedAnimationPreparationPreservedLive = animationFailure,
            samePackReloadCompleted,
            invalidStateReloadPreservedLive = true, invalidPlantReloadPreservedLive = true,
            stateRemapInstalledAndRestored = true,
            lateFailurePreservedLive = true, failedPersistencePreservedLive = true,
            failedCandidatesReleased = true,
            liveBindingsGeneration = textures.BlockBindings?.Generation,
            terrainUsesModelArray = ReferenceEquals(textures.TerrainArray.Texture, textures.BlockModels.Current?.Texture),
            note = "All 16 stone, slab and double-slab metadata values use validated client state definitions."
        };
    }

    private sealed class StateOverridePack(string statePath, string json, Func<string, Stream?> openOriginal) : TexturePack
    {
        internal override Stream? OpenReloadOverride(string path) =>
            path == statePath ? new MemoryStream(Encoding.UTF8.GetBytes(json)) : openOriginal(path);
    }

    private sealed class BrokenResourcePack(string brokenPath) : TexturePack
    {
        internal override Stream? OpenReloadOverride(string path) => path == brokenPath ? new MemoryStream([1, 2, 3]) : null;
    }

    private sealed class BrokenAnimationPack(TexturePack source) : TexturePack
    {
        public override Stream? GetResourceAsStream(string path) => path == "custom_fire_e_w.png"
            ? new MemoryStream([1, 2, 3]) : source.GetResourceAsStream(path);
        internal override Stream? OpenReloadOverride(string path) => path == "custom_fire_e_w.png"
            ? new MemoryStream([1, 2, 3]) : source.OpenReloadOverride(path);
    }
}
