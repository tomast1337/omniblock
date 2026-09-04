using System.Globalization;

namespace OmniBlock.Rules;

public abstract class GameRule<T>(ResourceLocation key, T defaultValue, string category, string description)
    : IGameRule<T> where T : IRuleValue
{
    public ResourceLocation Key { get; } = key;
    public Type ValueType => typeof(T);
    public T DefaultValue { get; } = defaultValue;
    IRuleValue IGameRule.DefaultValue => DefaultValue;
    public string Category { get; } = category;
    public string Description { get; } = description;

    IRuleValue IGameRule.Deserialize(string raw) => Deserialize(raw);
    string IGameRule.Serialize(IRuleValue value) => Serialize((T)value);

    public abstract T Deserialize(string raw);
    public abstract string Serialize(T value);
}

public sealed class BoolRule(ResourceLocation key, bool defaultValue, string category = "general", string description = "")
    : GameRule<BoolValue>(key, defaultValue, category, description)
{
    public override BoolValue Deserialize(string raw) =>
        new(raw.Equals("true", StringComparison.OrdinalIgnoreCase));

    public override string Serialize(BoolValue value) => value.ToString();
}

public sealed class IntRule(
    ResourceLocation key,
    int defaultValue,
    int min = int.MinValue,
    int max = int.MaxValue,
    string category = "general",
    string description = "")
    : GameRule<IntValue>(key, defaultValue, category, description)
{
    public int Min { get; } = min;
    public int Max { get; } = max;

    public override IntValue Deserialize(string raw)
    {
        var v = int.TryParse(raw, out var n) ? n : DefaultValue.Value;
        return new IntValue(Math.Clamp(v, Min, Max));
    }

    public override string Serialize(IntValue value) => value.ToString();
}

public sealed class FloatRule(
    ResourceLocation key,
    float defaultValue,
    float min = float.MinValue,
    float max = float.MaxValue,
    string category = "general",
    string description = "")
    : GameRule<FloatValue>(key, defaultValue, category, description)
{
    public float Min { get; } = min;
    public float Max { get; } = max;

    public override FloatValue Deserialize(string raw)
    {
        var v = float.TryParse(raw, CultureInfo.InvariantCulture, out var f) ? f : DefaultValue.Value;
        return new FloatValue(Math.Clamp(v, Min, Max));
    }

    public override string Serialize(FloatValue value) => value.ToString();
}

public sealed class StringRule(
    ResourceLocation key,
    string defaultValue,
    string category = "general",
    string description = "")
    : GameRule<StringValue>(key, defaultValue, category, description)
{
    public override StringValue Deserialize(string raw) => new(raw);
    public override string Serialize(StringValue value) => value.Value;
}

public sealed class EnumRule<T>(
    ResourceLocation key,
    T defaultValue,
    string category = "general",
    string description = "")
    : GameRule<EnumValue<T>>(key, new EnumValue<T>(defaultValue), category, description)
    where T : struct, Enum
{
    public override EnumValue<T> Deserialize(string raw)
    {
        if (Enum.TryParse(raw, true, out T result))
            return new EnumValue<T>(result);
        return DefaultValue;
    }

    public override string Serialize(EnumValue<T> value) => value.Value.ToString();
}
