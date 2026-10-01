namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// The four immutable, two-sided quads used by reeds and other crossed plants. Terrain,
/// inventory, and fine LOD translate this same local-space geometry; texture, tint and light
/// remain properties of the block and the presentation being built.
/// </summary>
internal static class CrossedPlantGeometry
{
    internal readonly record struct Vertex(float X, float Y, float Z, float U, float V);
    internal readonly record struct Quad(Vertex A, Vertex B, Vertex C, Vertex D);

    private const float Near = .05f;
    private const float Far = .95f;

    private static readonly Quad[] s_quads =
    [
        new(new(Near, 1, Near, 0, 0), new(Near, 0, Near, 0, 1),
            new(Far, 0, Far, 1, 1), new(Far, 1, Far, 1, 0)),
        new(new(Far, 1, Far, 0, 0), new(Far, 0, Far, 0, 1),
            new(Near, 0, Near, 1, 1), new(Near, 1, Near, 1, 0)),
        new(new(Near, 1, Far, 0, 0), new(Near, 0, Far, 0, 1),
            new(Far, 0, Near, 1, 1), new(Far, 1, Near, 1, 0)),
        new(new(Far, 1, Near, 0, 0), new(Far, 0, Near, 0, 1),
            new(Near, 0, Far, 1, 1), new(Near, 1, Far, 1, 0))
    ];

    internal static ReadOnlySpan<Quad> Quads => s_quads;

    internal static (float X, float Y, float Z) Offset(int x, int y, int z, bool tallGrass)
    {
        if (!tallGrass) return (0, 0, 0);
        var hash = (x * 3129871L) ^ (z * 116129781L) ^ y;
        hash = hash * hash * 42317861L + hash * 11L;
        return ((((hash >> 16) & 15L) / 15.0F - 0.5F) * 0.5F,
            (((hash >> 20) & 15L) / 15.0F - 1.0F) * 0.2F,
            (((hash >> 24) & 15L) / 15.0F - 0.5F) * 0.5F);
    }
}
