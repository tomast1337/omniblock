using OmniBlock.Client.Rendering.Blocks.Models;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>Immutable render-resource snapshot carried by asynchronous LOD mesh jobs.</summary>
internal readonly record struct TerrainLodResourceIdentity(long Generation, BlockModelBindings? Models)
{
    internal bool Matches(in TerrainLodResourceIdentity current) =>
        Generation == current.Generation && ReferenceEquals(Models, current.Models);
}
