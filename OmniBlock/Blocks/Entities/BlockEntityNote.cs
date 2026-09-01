using OmniBlock.Blocks.Materials;
using OmniBlock.NBT;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Entities;

internal class BlockEntityNote : BlockEntity
{
    public sbyte note;
    public bool powered = false;
    protected override BlockEntityType Type => Note;

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        nbt.SetByte("note", note);
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        note = nbt.GetByte("note");
        if (note < 0) note = 0;
        if (note > 24) note = 24;
    }

    public void CycleNote()
    {
        note = (sbyte)((note + 1) % 25);
        MarkDirty();
    }

    public void PlayNote(IWorldContext level, int x, int y, int z)
    {
        if (level.Reader.GetMaterial(x, y + 1, z) != Material.Air) return;
        var material = level.Reader.GetMaterial(x, y - 1, z);
        byte instrument = 0;
        if (material == Material.Stone) instrument = 1;
        if (material == Material.Sand) instrument = 2;
        if (material == Material.Glass) instrument = 3;
        if (material == Material.Wood) instrument = 4;
        level.Broadcaster.PlayNote(x, y, z, instrument, note);
    }
}