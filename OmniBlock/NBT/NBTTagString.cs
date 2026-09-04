namespace OmniBlock.NBT;

internal sealed class NBTTagString : NBTBase
{
    public NBTTagString()
    {
    }

    public NBTTagString(string value) => Value = value;
    public string Value { get; set; } = string.Empty;

    public override void WriteTagContents(Stream output) => output.WriteString(Value);

    public override void ReadTagContents(Stream input) => Value = input.ReadString();

    public override byte GetTagType() => 8;

    public override string ToString() => Value;
}
