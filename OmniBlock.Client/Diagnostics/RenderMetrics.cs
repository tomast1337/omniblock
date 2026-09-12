using OmniBlock.Diagnostics;

namespace OmniBlock.Client.Diagnostics;

internal static class RenderMetrics
{
    public static readonly MetricHandle<int> ChunksTotal = MetricRegistry.Register<int>("render:chunks.total");
    public static readonly MetricHandle<int> ChunksFrustum = MetricRegistry.Register<int>("render:chunks.frustum");
    public static readonly MetricHandle<int> ChunksOccluded = MetricRegistry.Register<int>("render:chunks.occluded");
    public static readonly MetricHandle<int> ChunksRendered = MetricRegistry.Register<int>("render:chunks.rendered");
    public static readonly MetricHandle<int> GeometryUploads = MetricRegistry.Register<int>("render:chunks.geometry_uploads");
    public static readonly MetricHandle<int> LightUploads = MetricRegistry.Register<int>("render:chunks.light_uploads");
    public static readonly MetricHandle<int> SolidDraws = MetricRegistry.Register<int>("render:chunks.solid_draws");
    public static readonly MetricHandle<int> TranslucentDraws = MetricRegistry.Register<int>("render:chunks.translucent_draws");
    public static readonly MetricHandle<int> VisibilityCandidates = MetricRegistry.Register<int>("render:chunks.visibility_candidates");
    public static readonly MetricHandle<int> SpatialRegionTests = MetricRegistry.Register<int>("render:chunks.spatial_region_tests");
    public static readonly MetricHandle<int> SpatialColumnTests = MetricRegistry.Register<int>("render:chunks.spatial_column_tests");
    public static readonly MetricHandle<int> SpatialSectionTests = MetricRegistry.Register<int>("render:chunks.spatial_section_tests");
    public static readonly MetricHandle<int> FrustumTests = MetricRegistry.Register<int>("render:chunks.frustum_tests");
    public static readonly MetricHandle<int> PortalVisited = MetricRegistry.Register<int>("render:chunks.portal_visited");
    public static readonly MetricHandle<int> DisconnectedSeeds = MetricRegistry.Register<int>("render:chunks.disconnected_seeds");
    public static readonly MetricHandle<int> SafetyRescued = MetricRegistry.Register<int>("render:chunks.safety_rescued");
    public static readonly MetricHandle<int> IncompleteAdjacencyRescued = MetricRegistry.Register<int>("render:chunks.rescue.incomplete_adjacency");
    public static readonly MetricHandle<int> NewPresentationRescued = MetricRegistry.Register<int>("render:chunks.rescue.new_presentation");
    public static readonly MetricHandle<int> PresentationRegressionRescued = MetricRegistry.Register<int>("render:chunks.rescue.presentation_regression");
    public static readonly MetricHandle<int> OldestSafetyRescueFrames = MetricRegistry.Register<int>("render:chunks.rescue.oldest_frames");
    public static readonly MetricHandle<int> ResidentSolidLayers = MetricRegistry.Register<int>("render:chunks.resident_solid_layers");
    public static readonly MetricHandle<int> ResidentTranslucentLayers = MetricRegistry.Register<int>("render:chunks.resident_translucent_layers");
    public static readonly MetricHandle<int> PresentedSolidLayers = MetricRegistry.Register<int>("render:chunks.presented_solid_layers");
    public static readonly MetricHandle<int> PresentedTranslucentLayers = MetricRegistry.Register<int>("render:chunks.presented_translucent_layers");
    public static readonly MetricHandle<int> EmptyLayersSubmitted = MetricRegistry.Register<int>("render:chunks.empty_layers_submitted");
    public static readonly MetricHandle<int> TerrainDrawCalls = MetricRegistry.Register<int>("render:chunks.terrain_draw_calls");
    public static readonly MetricHandle<int> TerrainUniformEntries = MetricRegistry.Register<int>("render:chunks.terrain_uniform_entries");
    public static readonly MetricHandle<double> FindVisibleMs = MetricRegistry.Register<double>("render:chunks.find_visible_ms");
    public static readonly MetricHandle<double> TerrainSubmitCpuMs = MetricRegistry.Register<double>("render:chunks.terrain_submit_cpu_ms");
    public static readonly MetricHandle<int> MeshVersionAllocated = MetricRegistry.Register<int>("render:mesh.version.allocated");
    public static readonly MetricHandle<int> MeshVersionReleased = MetricRegistry.Register<int>("render:mesh.version.released");
    public static readonly MetricHandle<int> MeshActive = MetricRegistry.Register<int>("render:mesh.active");
    public static readonly MetricHandle<int> MeshMinBytes = MetricRegistry.Register<int>("render:mesh.min_bytes");
    public static readonly MetricHandle<int> MeshMaxBytes = MetricRegistry.Register<int>("render:mesh.max_bytes");
    public static readonly MetricHandle<int> MeshAvgBytes = MetricRegistry.Register<int>("render:mesh.avg_bytes");
    public static readonly MetricHandle<int> TextureBindsLastFrame = MetricRegistry.Register<int>("render:texture.binds_last_frame");
    public static readonly MetricHandle<float> TextureAvgBinds = MetricRegistry.Register<float>("render:texture.avg_binds");
    public static readonly MetricHandle<int> TextureActive = MetricRegistry.Register<int>("render:texture.active");

    public static readonly MetricHandle<int> EntitiesRendered = MetricRegistry.Register<int>("render:entities.rendered");
    public static readonly MetricHandle<int> EntitiesHidden = MetricRegistry.Register<int>("render:entities.hidden");
    public static readonly MetricHandle<int> EntitiesTotal = MetricRegistry.Register<int>("render:entities.total");
    public static readonly MetricHandle<int> ParticlesActive = MetricRegistry.Register<int>("render:particles.active");

    static RenderMetrics()
    {
    }
}
