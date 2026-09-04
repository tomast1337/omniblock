using System.Globalization;

namespace OmniBlock.NBT;

internal sealed class NBTTagDouble : NBTBase
{
    public NBTTagDouble()
    {
    }

    public NBTTagDouble(double value) => Value = value;
    public double Value { get; set; }

    public override void WriteTagContents(Stream output) => output.WriteDouble(Value);

    public override void ReadTagContents(Stream input) => Value = input.ReadDouble();

    public override byte GetTagType() => 6;

    public override string ToString() => Value.ToString(CultureInfo.CurrentCulture);
}
