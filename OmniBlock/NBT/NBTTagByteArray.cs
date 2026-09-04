namespace OmniBlock.NBT;

internal sealed class NBTTagByteArray : NBTBase
{
    public NBTTagByteArray()
    {
    }

    public NBTTagByteArray(byte[] value) => Values = value;
    public byte[] Values { get; set; } = [];

    public override void WriteTagContents(Stream output)
    {
        output.WriteInt(Values.Length);
        output.Write(Values);
    }

    public override void ReadTagContents(Stream input)
    {
        var length = input.ReadInt();
        Values = new byte[length];
        input.ReadExactly(Values);
    }

    public override byte GetTagType() => 7;

    public override string ToString() => $"[{Values.Length} bytes]";
}
