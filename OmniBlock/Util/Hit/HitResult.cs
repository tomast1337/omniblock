using OmniBlock.Entities;
using OmniBlock.Util.Maths;

namespace OmniBlock.Util.Hit;

public struct HitResult(int blockX, int blockY, int blockZ, int side, Vec3D pos, HitResultType type)
{
    public readonly HitResultType Type = type;
    public int BlockX = blockX;
    public int BlockY = blockY;
    public int BlockZ = blockZ;
    public readonly int Side = side;
    public Vec3D Pos = pos;
    public readonly Entity? Entity;

    public HitResult(HitResultType type) : this(0, 0, 0, 0, new Vec3D(), type)
    {
    }

    public HitResult(Entity entity) : this(0, 0, 0, 0, new Vec3D(entity.X, entity.Y, entity.Z), HitResultType.Entity) => Entity = entity;
}
