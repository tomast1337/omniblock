using OmniBlock.NBT;
using OmniBlock.Network.Messages;

namespace OmniBlock.Blocks.Entities;

public class BlockEntitySign : BlockEntity
{
    private bool _editable = true;
    protected override BlockEntityType Type => Sign;
    public string[] Texts { get; set; } = ["", "", "", ""];
    public int CurrentRow { get; set; } = -1;

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        nbt.SetString("Text1", Texts[0]);
        nbt.SetString("Text2", Texts[1]);
        nbt.SetString("Text3", Texts[2]);
        nbt.SetString("Text4", Texts[3]);
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        _editable = false;
        base.ReadNbt(nbt);

        for (var line = 0; line < 4; ++line)
        {
            Texts[line] = nbt.GetString("Text" + (line + 1));
            if (Texts[line].Length > 15) Texts[line] = Texts[line].Substring(0, 15);
        }
    }

    public override Message? CreateUpdateMessage() => new UpdateSignMessage
    {
        X = X,
        Y = (short)Y,
        Z = Z,
        Lines = Texts
    };

    public bool IsEditable() => _editable;

    public void SetEditable(bool editable) => _editable = editable;
}
