namespace OmniBlock.NBT;

internal sealed class NBTTagByte : NBTBase
{
    public NBTTagByte()
    {
    }

    public NBTTagByte(sbyte value) => Value = value;
    public sbyte Value { get; set; }

    public override void WriteTagContents(Stream output) => output.WriteByte((byte)Value);

    public override void ReadTagContents(Stream input) => Value = (sbyte)input.ReadByte();

    public override byte GetTagType() => 1;

    public override string ToString() => Value.ToString();
}
