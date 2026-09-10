namespace OmniBlock.Client.Rendering.Core;

/// <summary>
///     Where the frame is in time, for the shaders that animate from it.
/// </summary>
/// <remarks>
///     Fog used to live here too, written alongside every <c>Fog</c> call so the fallback shader and
///     the hand-written ones would agree. They now read
///     <see cref="RenderSystem.Fog" />, which is the same value rather than a copy that has to be kept
///     in step.
/// </remarks>
public class CommonShaderInfo
{
    public float DayTime;
    public float DeltaTime;
    public float Time;
}
