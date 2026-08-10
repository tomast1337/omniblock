namespace OmniBlock.Entities.State;

/// <summary>
///     The set of state slots a single <see cref="EntityType" />'s behaviors need. Built once at
///     load while those behaviors are constructed, then used to size every instance's
///     <see cref="EntityState" />.
/// </summary>
public sealed class EntityStateLayout
{
    private readonly List<(int Index, bool Value)> _boolDefaults = [];
    private readonly List<(int Index, double Value)> _doubleDefaults = [];
    private readonly List<(int Index, float Value)> _floatDefaults = [];

    private readonly List<(int Index, int Value)> _intDefaults = [];
    private int _bools;
    private int _doubles;
    private int _floats;
    private int _ints;
    private int _longs;
    private int _refs;

    public StateHandle<int> DeclareInt(int initial = 0)
    {
        if (initial != 0)
        {
            _intDefaults.Add((_ints, initial));
        }

        return new StateHandle<int>(_ints++);
    }

    public StateHandle<long> DeclareLong() => new(_longs++);

    public StateHandle<float> DeclareFloat(float initial = 0.0F)
    {
        if (initial != 0.0F)
        {
            _floatDefaults.Add((_floats, initial));
        }

        return new StateHandle<float>(_floats++);
    }

    public StateHandle<double> DeclareDouble(double initial = 0.0D)
    {
        if (initial != 0.0D)
        {
            _doubleDefaults.Add((_doubles, initial));
        }

        return new StateHandle<double>(_doubles++);
    }

    public StateHandle<bool> DeclareBool(bool initial = false)
    {
        if (initial)
        {
            _boolDefaults.Add((_bools, initial));
        }

        return new StateHandle<bool>(_bools++);
    }

    /// <summary>Declares a reference-typed slot (an entity, a string, an item stack).</summary>
    public StateHandle<T> DeclareRef<T>() where T : class => new(_refs++);

    public EntityState Create()
    {
        EntityState state = new(_ints, _longs, _floats, _doubles, _bools, _refs);

        foreach ((int index, int value) in _intDefaults)
        {
            state.SetIntRaw(index, value);
        }

        foreach ((int index, float value) in _floatDefaults)
        {
            state.SetFloatRaw(index, value);
        }

        foreach ((int index, double value) in _doubleDefaults)
        {
            state.SetDoubleRaw(index, value);
        }

        foreach ((int index, bool value) in _boolDefaults)
        {
            state.SetBoolRaw(index, value);
        }

        return state;
    }
}
