using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

public struct ChunkRenderParams
{
    public ICuller Camera;
    public Matrix4X4<float> ModelView;
    public Matrix4X4<float> Projection;
    public Vector3D<double> ViewPos;
    public int RenderDistance;
    public long Ticks;
    public float PartialTicks;
    public float DeltaTime;
    public float VerticalFovDegrees;
    public int ViewportHeight;
    public bool ChunkFade;
    public bool RenderOccluded;
}
