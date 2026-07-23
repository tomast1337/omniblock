using System.Runtime.InteropServices;

namespace BetaSharp.Client.Rendering.UI;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 20)]
public struct UIVertex
{
    public float X, Y;
    public float U, V;
    public uint Rgba;
}
