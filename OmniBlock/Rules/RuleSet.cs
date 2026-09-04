using System.Collections.Concurrent;
using OmniBlock.NBT;

namespace OmniBlock.Rules;

public sealed class RuleSet(RuleRegistry registry)
{
    private readonly ConcurrentDictionary<ResourceLocation, IRuleValue> _values = new();

    public event Action<ResourceLocation, IRuleValue, IRuleValue>? RuleChanged;

    public T Get<T>(IGameRule<T> rule) where T : IRuleValue =>
        _values.TryGetValue(rule.Key, out var v) ? (T)v : rule.DefaultValue;

    public IRuleValue Get(ResourceLocation key)
    {
        var rule = registry.Get(key);
        return _values.TryGetValue(key, out var v) ? v : rule.DefaultValue;
    }

    public bool GetBool(IGameRule<BoolValue> rule) => (bool)Get(rule);
    public int GetInt(IGameRule<IntValue> rule) => (int)Get(rule);
    public float GetFloat(IGameRule<FloatValue> rule) => (float)Get(rule);
    public string GetString(IGameRule<StringValue> rule) => Get(rule);
    public TEnum GetEnum<TEnum>(IGameRule<EnumValue<TEnum>> rule) where TEnum : struct, Enum => (TEnum)Get(rule);

    public void Set<T>(IGameRule<T> rule, T value) where T : IRuleValue
    {
        var old = Get(rule);
        _values[rule.Key] = value;
        RuleChanged?.Invoke(rule.Key, old, value);
    }

    public bool TrySet(ResourceLocation key, string rawValue)
    {
        if (!registry.TryGet(key, out var rule)) return false;
        var newValue = rule.Deserialize(rawValue);
        var old = _values.TryGetValue(key, out var existing) ? existing : rule.DefaultValue;
        _values[key] = newValue;
        RuleChanged?.Invoke(key, old, newValue);
        return true;
    }

    public void Reset(ResourceLocation key) => _values.TryRemove(key, out _);

    public void ResetAll() => _values.Clear();

    public Dictionary<string, string> Serialize()
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in _values)
        {
            var rule = registry.Get(key);
            result[key.ToString()] = rule.Serialize(value);
        }

        return result;
    }

    public void Deserialize(Dictionary<string, string> data)
    {
        foreach (var (rawKey, rawValue) in data)
            TrySet(ResourceLocation.Parse(rawKey), rawValue);
    }

    public void WriteToNBT(NBTTagCompound nbt)
    {
        foreach (var (rule, value) in NonDefaultValues())
        {
            nbt.SetString(rule.Key.ToString(), rule.Serialize(value));
        }
    }

    public static RuleSet FromNBT(RuleRegistry registry, NBTTagCompound nbt)
    {
        RuleSet ruleSet = new(registry);
        foreach (var (key, value) in nbt.Dictionary)
        {
            if (value is NBTTagString str)
                ruleSet.TrySet(ResourceLocation.Parse(key), str.Value);
        }

        return ruleSet;
    }

    public IEnumerable<(IGameRule Rule, IRuleValue Value)> NonDefaultValues()
    {
        foreach (var (key, value) in _values)
        {
            var rule = registry.Get(key);
            if (!value.Equals(rule.DefaultValue))
                yield return (rule, value);
        }
    }
}
