using BetaSharp.Entities.State;
using BetaSharp.NBT;

namespace BetaSharp.Entities;

/// <summary>
///     Saving and loading. <see cref="Write" /> and <see cref="Read" /> handle what every entity
///     has; <see cref="WriteNbt" /> and <see cref="ReadNbt" /> are the class's own share, and the
///     composed <see cref="IEntityPersistence" /> slot runs last so it can override both.
/// </summary>
public abstract partial class Entity
{
    public void Write(NBTTagCompound nbt)
    {
        nbt.SetTag("Pos", newDoubleNbtList(X, Y + CameraOffset, Z));
        nbt.SetTag("Motion", newDoubleNbtList(VelocityX, VelocityY, VelocityZ));
        nbt.SetTag("Rotation", newFloatNbtList(Yaw, Pitch));
        nbt.SetFloat("FallDistance", FallDistance);
        nbt.SetShort("Fire", (short)FireTicks);
        nbt.SetShort("Air", (short)Air);
        nbt.SetBoolean("OnGround", OnGround);

        SyncedPropertyFactory.Write(DataSynchronizer, SyncedDeclarations, nbt);

        // Last, so composed persistence has the final say over what the class itself wrote.
        WriteNbt(nbt);
        Persistence?.OnWriteNbt(this, nbt);
    }

    public void Read(NBTTagCompound nbt)
    {
        NBTTagList pos = nbt.GetTagList("Pos");
        NBTTagList mot = nbt.GetTagList("Motion");
        NBTTagList rot = nbt.GetTagList("Rotation");

        VelocityX = ((NBTTagDouble)mot.TagAt(0)).Value;
        VelocityY = ((NBTTagDouble)mot.TagAt(1)).Value;
        VelocityZ = ((NBTTagDouble)mot.TagAt(2)).Value;

        if (Math.Abs(VelocityX) > 10.0D)
        {
            VelocityX = 0.0D;
        }

        if (Math.Abs(VelocityY) > 10.0D)
        {
            VelocityY = 0.0D;
        }

        if (Math.Abs(VelocityZ) > 10.0D)
        {
            VelocityZ = 0.0D;
        }

        PrevX = LastTickX = X = ((NBTTagDouble)pos.TagAt(0)).Value;
        PrevY = LastTickY = Y = ((NBTTagDouble)pos.TagAt(1)).Value;
        PrevZ = LastTickZ = Z = ((NBTTagDouble)pos.TagAt(2)).Value;

        PrevYaw = Yaw = ((NBTTagFloat)rot.TagAt(0)).Value;
        PrevPitch = Pitch = ((NBTTagFloat)rot.TagAt(1)).Value;

        FallDistance = nbt.GetFloat("FallDistance");
        FireTicks = nbt.GetShort("Fire");
        Air = nbt.GetShort("Air");
        OnGround = nbt.GetBoolean("OnGround");

        SetPosition(X, Y, Z);
        SetRotation(Yaw, Pitch);

        SyncedPropertyFactory.Read(DataSynchronizer, SyncedDeclarations, nbt);

        // Last for the same reason as writing, and it matters here: restoring a slime's size resets
        // its health from that size, so it must land after the health the class just read back.
        ReadNbt(nbt);
        Persistence?.OnReadNbt(this, nbt);
    }

    public bool SaveSelfNbt(NBTTagCompound nbt)
    {
        string? id = GetRegistryEntry();
        if (Dead || id == null)
        {
            return false;
        }

        nbt.SetString("id", id);
        Write(nbt);
        return true;
    }

    private string? GetRegistryEntry() => Type?.Id;

    protected abstract void ReadNbt(NBTTagCompound nbt);

    protected abstract void WriteNbt(NBTTagCompound nbt);

    private static NBTTagList newDoubleNbtList(params double[] arr)
    {
        NBTTagList nbt = new();
        foreach (double t in arr)
        {
            nbt.SetTag(new NBTTagDouble(t));
        }

        return nbt;
    }

    private static NBTTagList newFloatNbtList(params float[] arr)
    {
        NBTTagList nbt = new();
        foreach (float t in arr)
        {
            nbt.SetTag(new NBTTagFloat(t));
        }

        return nbt;
    }
}
