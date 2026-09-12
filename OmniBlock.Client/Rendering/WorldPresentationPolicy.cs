using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Particles;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering;

/// <summary>
///     Client-only limits for non-terrain world presentation. These bounds deliberately do not
///     participate in simulation, networking, saves, or content identity.
/// </summary>
internal readonly record struct WorldPresentationPolicy(
    double EntityDistance,
    double BlockEntityDistance,
    double ParticleDistance,
    int MaxParticleInstances,
    int WeatherRadius,
    float WeatherSpawnDensity,
    int TranslucentSortIntervalFrames)
{
    public double EntityDistanceSquared => EntityDistance * EntityDistance;
    public double BlockEntityDistanceSquared => BlockEntityDistance * BlockEntityDistance;
    public double ParticleDistanceSquared => ParticleDistance * ParticleDistance;

    public static WorldPresentationPolicy From(GameOptions options) =>
        From(options.PresentationQuality, options.RenderDistance);

    internal static WorldPresentationPolicy From(int quality, int renderDistance)
    {
        var terrainDistance = Math.Max(1, renderDistance) * 16.0;
        return quality switch
        {
            0 => new(
                Math.Min(terrainDistance, 64.0),
                Math.Min(terrainDistance, 48.0),
                24.0,
                1_000,
                6,
                0.35f,
                1),
            2 => new(
                Math.Min(terrainDistance, 256.0),
                Math.Min(terrainDistance, 128.0),
                64.0,
                ParticleBuffer.MaxParticles,
                10,
                1.0f,
                1),
            _ => new(
                Math.Min(terrainDistance, 128.0),
                Math.Min(terrainDistance, 80.0),
                40.0,
                2_500,
                8,
                0.65f,
                1)
        };
    }

    public bool ShouldRenderEntity(Entity entity, Vec3D cameraPosition) =>
        entity.GetSquaredDistance(cameraPosition.X, cameraPosition.Y, cameraPosition.Z) <=
        EntityDistanceSquared && entity.ShouldRender(cameraPosition);

    public bool ShouldRenderBlockEntity(double squaredDistance) =>
        squaredDistance <= BlockEntityDistanceSquared;

    public bool ShouldRenderParticle(double dx, double dy, double dz) =>
        dx * dx + dy * dy + dz * dz <= ParticleDistanceSquared;
}
