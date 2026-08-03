namespace BetaSharp.Client.UI.Layout.Flexbox;

public class Value
{
    public Unit unit;
    public float value;

    public Value(float v, Unit u)
    {
        value = v;
        unit = u;
    }

    public static Value UndefinedValue => new(float.NaN, Unit.Undefined);

    public static void CopyValue(Value[] dest, Value[] src)
    {
        for (int i = 0; i < src.Length; i++)
        {
            dest[i].value = src[i].value;
            dest[i].unit = src[i].unit;
        }
    }

    public Value Clone() => new(value, unit);
}
