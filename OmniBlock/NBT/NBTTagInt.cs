namespace OmniBlock.NBT;

internal sealed class NBTTagInt : NBTBase
{
    public NBTTagInt()
    {
    }

    public NBTTagInt(int value) => Value = value;
    public int Value { get; set; }

    public override void WriteTagContents(Stream output) => output.WriteInt(Value);

    public override void ReadTagContents(Stream input) => Value = input.ReadInt();

    public override byte GetTagType() => 3;

    public override string ToString() => Value.ToString();
}
