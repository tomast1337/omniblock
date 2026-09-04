namespace OmniBlock.NBT;

internal sealed class NBTTagLong : NBTBase
{
    public NBTTagLong()
    {
    }

    public NBTTagLong(long value) => Value = value;
    public long Value { get; set; }

    public override void WriteTagContents(Stream output) => output.WriteLong(Value);

    public override void ReadTagContents(Stream input) => Value = input.ReadLong();

    public override byte GetTagType() => 4;

    public override string ToString() => Value.ToString();
}
