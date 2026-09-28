using System.Text;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Resource.Pack;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Only invoked by the restricted gallery E2E controller, not ordinary gameplay.</summary>
internal static class BlockModelResourceSmoke
{
    public static object Run(TextureManager textures, TexturePack pack, bool mipmaps)
    {
        using var sources = BlockModelPackResources.Open(pack);
        var root = RenderResourceId.Parse("omniblock:block/model_probe");
        var prepared = PreparedBlockModelResources.Build([root], sources.FixedLayers(), sources.OpenOverride, sources.OpenBuiltin);
        var slot = textures.BlockModels;
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
            note = "Shadow resource slot: gameplay geometry still uses the existing renderer."
        };
    }
}
