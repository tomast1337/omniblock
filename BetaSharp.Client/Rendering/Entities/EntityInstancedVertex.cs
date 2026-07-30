using System.Runtime.InteropServices;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>One vertex of a <see cref="Models.ModelPart"/>'s static local-space geometry.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 40)]
public struct EntityInstancedVertex
{
    public float X, Y, Z; // 12 bytes, local space
    public float U, V; // 8 bytes
    public float NX, NY, NZ; // 12 bytes, local face normal
    public uint LocalSlot; // 4 bytes, index into the owning instance's pose-matrix array
    public uint PartId; // 4 bytes, symbolic model part (see EntityShaderIds); 0 when unmapped
}
