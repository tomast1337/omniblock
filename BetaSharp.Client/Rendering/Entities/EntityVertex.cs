using System.Runtime.InteropServices;

namespace BetaSharp.Client.Rendering.Entities;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 28)]
public struct EntityVertex
{
    public float X, Y, Z; // 12 bytes, already view-space (baked from the current GL modelview matrix)
    public float U, V; // 8 bytes
    public uint Color; // 4 bytes, packed RGBA, lighting + glColor tint already baked in
    public uint PartId; // 4 bytes, symbolic model part (see EntityShaderIds); 0 when unmapped
}
