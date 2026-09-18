using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
///     Fully uploaded spatial tile candidate. Every page uses the same regional vertex arenas as
///     exact terrain and retains packed directional ranges for camera-side submission filtering.
/// </summary>
internal sealed class TerrainLodSpatialGpuPresentation : IDisposable
{
    private TerrainLodSpatialGpuPresentation(
        TerrainLodTileKey key,
        string canonicalHash,
        GpuPage[] pages,
        long estimatedBytes)
    {
        Key = key;
        CanonicalHash = canonicalHash;
        Pages = pages;
        EstimatedBytes = estimatedBytes;
    }

    public TerrainLodTileKey Key { get; }
    public string CanonicalHash { get; }
    public IReadOnlyList<GpuPage> Pages { get; }
    public long EstimatedBytes { get; }
    public bool HasSolidGeometry => Pages.Any(static page => page.Solid is not null);
    public bool HasTranslucentGeometry => Pages.Any(static page => page.Translucent is not null);

    public static TerrainLodSpatialGpuPresentation Create(
        WebGpuDevice device,
        TerrainGpuArenaSet arenas,
        TerrainLodSpatialMeshData data)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(arenas);
        ArgumentNullException.ThrowIfNull(data);
        List<GpuPage> pages = [];
        try
        {
            foreach (var page in data.Pages)
                pages.Add(GpuPage.Create(device, arenas, page));
            return new TerrainLodSpatialGpuPresentation(
                data.Key, data.CanonicalHash, [.. pages], data.EstimatedBytes);
        }
        catch
        {
            foreach (var page in pages) page.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var page in Pages) page.Dispose();
    }

    internal sealed class GpuPage : IDisposable
    {
        private GpuPage(
            TerrainLodSpatialMeshPageKey key,
            Vector3D<int> origin,
            TerrainChunkQuadMesh? solid,
            TerrainChunkQuadMesh? translucent,
            ChunkDirectionalRanges solidRanges,
            ChunkDirectionalRanges translucentRanges)
        {
            Key = key;
            Origin = origin;
            Solid = solid;
            Translucent = translucent;
            SolidRanges = solidRanges;
            TranslucentRanges = translucentRanges;
        }

        public TerrainLodSpatialMeshPageKey Key { get; }
        public Vector3D<int> Origin { get; }
        public TerrainChunkQuadMesh? Solid { get; }
        public TerrainChunkQuadMesh? Translucent { get; }
        public ChunkDirectionalRanges SolidRanges { get; }
        public ChunkDirectionalRanges TranslucentRanges { get; }

        public static GpuPage Create(
            WebGpuDevice device,
            TerrainGpuArenaSet arenas,
            TerrainLodSpatialMeshPage page)
        {
            var origin = new Vector3D<int>(page.OriginX, page.OriginY, page.OriginZ);
            var region = TerrainRenderRegionKey.FromSectionPosition(origin);
            TerrainChunkQuadMesh? solid = null;
            TerrainChunkQuadMesh? translucent = null;
            try
            {
                if (page.Vertices.Length > 0)
                    solid = TerrainChunkQuadMesh.Create(
                        device, arenas, region, page.Vertices, page.Lights);
                if (page.TranslucentVertices.Length > 0)
                    translucent = TerrainChunkQuadMesh.Create(
                        device, arenas, region,
                        page.TranslucentVertices, page.TranslucentLights);
                return new GpuPage(
                    page.Key, origin, solid, translucent,
                    page.SolidRanges, page.TranslucentRanges);
            }
            catch
            {
                solid?.Dispose();
                translucent?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Solid?.Dispose();
            Translucent?.Dispose();
        }
    }
}
