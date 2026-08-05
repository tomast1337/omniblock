using BetaSharp.Client.Options;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>
///     <see cref="ProgramSlot.Terrain" />: solid and cutout terrain.
/// </summary>
/// <remarks>
///     Chunk rendering owns a large amount of bespoke per-frame uniform state (ambient darkness, chunk
///     fade, the projection matrix, per-sub-chunk transforms) that has nothing to do with slot
///     resolution, so unlike <see cref="TexturedSlotProgram" /> this does not centralize uniform
///     uploads — <see cref="Shader" /> is exposed so <c>ChunkRenderer</c> keeps doing that itself
///     against the same instance. What this adds is that terrain finally resolves through
///     <see cref="SlotPrograms" /> like every other slot, instead of <c>ChunkRenderer</c> privately
///     constructing and binding a shader no pack can ever address by name.
/// </remarks>
internal sealed class TerrainSlotProgram : ISlotProgram, IDisposable
{
    public Shader Shader { get; }

    public TerrainSlotProgram(GameOptions options) =>
        Shader = new Shader(
            options.ShaderOptions.GetOrCreate("chunk"),
            "shaders/chunk.vert",
            "shaders/chunk.frag");

    public VertexLayoutKind VertexLayout => VertexLayoutKind.Chunk;

    public void Activate() => Shader.Bind();

    /// <inheritdoc cref="BasicSlotProgram.Deactivate" />
    public void Deactivate() => GLManager.GL.UseProgram(0);

    public void Dispose() => Shader.Dispose();
}
