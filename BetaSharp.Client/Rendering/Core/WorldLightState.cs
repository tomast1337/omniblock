namespace OmniBlock.Client.Rendering.Core;

/// <summary>What a shader needs to turn a vertex's light levels into a brightness.</summary>
/// <remarks>
///     Both belong to the world rather than to any draw: the darkness moves with the sun, and the
///     floor is a property of the dimension. Kept out of the geometry for the reason the light
///     levels themselves are — a mesh that baked either in would have to be rebuilt when the sun
///     moved, and a pack would have nothing left to relight.
/// </remarks>
/// <param name="AmbientDarkness">How far the sky channel is knocked down, in levels.</param>
/// <param name="LuminanceOffset">
///     The floor of the brightness curve: 0.05 in the overworld, 0.1 in the nether.
/// </param>
public readonly record struct WorldLightState(float AmbientDarkness, float LuminanceOffset)
{
    /// <summary>
    ///     Full daylight at the overworld's floor, for a draw with no world behind it. Under these a
    ///     vertex carrying full block light still comes out at full brightness, which is what the
    ///     menus and the inventory want.
    /// </summary>
    public static WorldLightState Default { get; } = new(0.0f, 0.05f);
}
