using System.Globalization;

namespace OmniBlock.NBT;

internal sealed class NBTTagFloat : NBTBase
{
    public NBTTagFloat()
    {
    }

    public NBTTagFloat(float value) => Value = value;
    public float Value { get; set; }

    public override void WriteTagContents(Stream output) => output.WriteFloat(Value);

    public override void ReadTagContents(Stream input) => Value = input.ReadFloat();

    public override byte GetTagType() => 5;

    public override string ToString() => Value.ToString(CultureInfo.CurrentCulture);
}
