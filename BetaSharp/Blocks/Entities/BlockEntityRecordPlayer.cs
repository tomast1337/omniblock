using BetaSharp.NBT;

namespace BetaSharp.Blocks.Entities;

internal class BlockEntityRecordPlayer : BlockEntity
{
    public override BlockEntityType Type => BlockEntity.RecordPlayer;
    public int recordId;

    public override void ReadNbt(NBTTagCompound nbt)
    {
        recordId = nbt.GetInteger("Record");
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        if (recordId > 0)
        {
            nbt.SetInteger("Record", recordId);
        }

    }
}
