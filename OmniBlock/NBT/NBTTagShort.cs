namespace OmniBlock.NBT;

internal sealed class NBTTagShort : NBTBase
{
    public NBTTagShort()
    {
    }

    public NBTTagShort(short value) => Value = value;
    public short Value { get; set; }

    public override void WriteTagContents(Stream output) => output.WriteShort(Value);

    public override void ReadTagContents(Stream input) => Value = input.ReadShort();

    public override byte GetTagType() => 2;

    public override string ToString() => Value.ToString();
}
