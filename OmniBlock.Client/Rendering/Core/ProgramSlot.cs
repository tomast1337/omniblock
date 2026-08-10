namespace OmniBlock.Client.Rendering.Core;

/// <summary>What is being drawn, named so that a shader pack can address it.</summary>
/// <remarks>
///     <para>
///         A slot is not a shader file. It is a name a draw carries, which the pack loader resolves
///         to whichever program is registered for it, or — far more often — for one of its ancestors
///         in the fallback chain (see <see cref="ProgramSlots" />). Declaring a slot therefore costs
///         an enum entry and nothing else, which is the point: the taxonomy can be as fine as the
///         draws really are without anyone having to write sixteen shaders.
///     </para>
///     <para>
///         The names track OptiFine/Iris's <c>gbuffers_*</c> programs deliberately, so that their
///         documentation applies here and a pack written for them lands close.
///         <see cref="ProgramSlots.PackName" /> is the name a pack file uses; these are the C#
///         spelling of the same thing. Slots for concepts Beta 1.7.3 does not have — beacons,
///         banners, the end portal — are left out rather than stubbed.
///     </para>
/// </remarks>
public enum ProgramSlot
{
    /// <summary>Untextured geometry. The root of the fallback chain: every other slot ends here.</summary>
    Basic,

    /// <summary>The selection box outline, chunk borders, debug lines.</summary>
    Line,

    /// <summary>Textured and tinted, unlit. Text, item sprites, maps, paintings, the loading screen.</summary>
    Textured,

    /// <summary>The sky dome.</summary>
    SkyBasic,

    /// <summary>Sun and moon.</summary>
    SkyTextured,

    Clouds,

    /// <summary>Textured, tinted and lit. The common case for anything in the world.</summary>
    TexturedLit,

    /// <summary>Rain and snow.</summary>
    Weather,

    /// <summary>Block and item geometry held in first person.</summary>
    Hand,

    /// <summary>Entity models.</summary>
    Entities,

    /// <summary>Overlays lit independently of the model under them: the charged creeper, spider eyes.</summary>
    EntitiesGlowing,

    /// <summary>Solid and cutout terrain.</summary>
    Terrain,

    /// <summary>The block-breaking crack overlay.</summary>
    DamagedBlock,

    /// <summary>Block entities — signs, chests, pistons.</summary>
    BlockEntity,

    /// <summary>The translucent block pass.</summary>
    Water,

    /// <summary>
    ///     UI widgets, item slots, the inventory mob preview. Has no Iris counterpart — Iris does not
    ///     shade the GUI — and exists here because the UI is already its own program.
    /// </summary>
    Gui,
}
