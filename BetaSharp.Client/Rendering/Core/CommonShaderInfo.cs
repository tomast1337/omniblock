using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.Core;

public class CommonShaderInfo
{
    /// <summary>
    /// 0 = Linear
    /// 1 = Exp
    /// </summary>
    public int FogMode;
    public float FogDensity;
    public float FogStart;
    public float FogEnd;
    public float Time;
    public float DeltaTime;
    public float DayTime;
    public Vector4D<float> FogColor = new();
}
