using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities;

public interface IEntity
{
    IWorldContext World { get; }
    Vec3D Position { get; }
    void Read(NBTTagCompound nbt);
    void Write(NBTTagCompound nbt);
    void Tick();
    int GetId();
}
