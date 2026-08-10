using OmniBlock.NBT;

namespace OmniBlock.Blocks.Entities;

internal class BlockEntityRecordPlayer : BlockEntity
{
    public int RecordId;
    protected override BlockEntityType Type => RecordPlayer;

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        RecordId = nbt.GetInteger("Record");
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        if (RecordId > 0) nbt.SetInteger("Record", RecordId);
    }
}
