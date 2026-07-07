using BetaSharp.NBT;

namespace BetaSharp.Blocks.Entities;

internal class BlockEntityRecordPlayer : BlockEntity
{
    public int RecordId;
    public override BlockEntityType Type => RecordPlayer;

    public override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        RecordId = nbt.GetInteger("Record");
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        if (RecordId > 0)
        {
            nbt.SetInteger("Record", RecordId);
        }
    }
}
