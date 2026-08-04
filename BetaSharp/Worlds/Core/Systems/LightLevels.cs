namespace BetaSharp.Worlds.Core.Systems;

/// <summary>The two light values for a cell, before either the ramp or the time of day.</summary>
/// <remarks>
///     <para>
///         Beta collapses these into one number as early as <c>Chunk.GetLight</c>, taking
///         <c>max(block, sky - ambientDarkness)</c> and handing the renderer a single level. That is
///         cheap and it is why the terrain shader has never needed to know what time it is — but it
///         also means nothing downstream can tell sunlight from torchlight, which is the first thing
///         a shader pack wants to do.
///     </para>
///     <para>
///         Kept pre-ramp on purpose. The ramp is not linear, so averaging ramped values across a
///         face's corners is not the same as ramping the averaged level, and only one of those can
///         happen in the shader. The levels travel; the ramp and the max happen at the end.
///     </para>
/// </remarks>
public readonly record struct LightLevels(byte Sky, byte Block)
{
    /// <summary>What a cell outside any loaded chunk reads as: full sun, no torch.</summary>
    public static LightLevels FullSky { get; } = new(15, 0);

    public static LightLevels Of(int sky, int block) =>
        new((byte)Math.Clamp(sky, 0, 15), (byte)Math.Clamp(block, 0, 15));

    /// <summary>The brighter of two cells, per channel.</summary>
    /// <remarks>
    ///     Per channel rather than on the collapsed value, which is what the fluid surface and the
    ///     slab/stairs cases used to do. The two disagree — a cell that wins on sky and loses on
    ///     block gave one answer collapsed and gives another here — and this is the reading that
    ///     survives having the channels separate at all.
    /// </remarks>
    public LightLevels Max(LightLevels other) =>
        new(System.Math.Max(Sky, other.Sky), System.Math.Max(Block, other.Block));

    /// <summary>Raises the block channel to a floor, for a block that emits its own light.</summary>
    public LightLevels WithBlockFloor(int minBlock) =>
        minBlock <= Block ? this : new(Sky, (byte)Math.Clamp(minBlock, 0, 15));
}
