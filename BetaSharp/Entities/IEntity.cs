using BetaSharp.NBT;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public interface IEntity
{
    void Read(NBTTagCompound nbt);
    void Write(NBTTagCompound nbt);
    void Tick();
    IWorldContext World { get; }
    Vec3D Position { get; }
    int GetId();
}
