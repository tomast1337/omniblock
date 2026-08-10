namespace OmniBlock.Entities.State;

/// <summary>
///     Per-entity storage for the slots its shared behaviors declared. Backed by right-sized typed
///     arrays, not a string-keyed dictionary: reads are an array index with no boxing and no lookup,
///     and a wrong-typed access does not compile.
/// </summary>
public sealed class EntityState
{
    private readonly bool[] _bools;
    private readonly double[] _doubles;
    private readonly float[] _floats;
    private readonly int[] _ints;
    private readonly long[] _longs;
    private readonly object?[] _refs;

    internal EntityState(int ints, int longs, int floats, int doubles, int bools, int refs)
    {
        _ints = ints == 0 ? [] : new int[ints];
        _longs = longs == 0 ? [] : new long[longs];
        _floats = floats == 0 ? [] : new float[floats];
        _doubles = doubles == 0 ? [] : new double[doubles];
        _bools = bools == 0 ? [] : new bool[bools];
        _refs = refs == 0 ? [] : new object?[refs];
    }

    public int this[StateHandle<int> handle]
    {
        get => _ints[handle.Index];
        set => _ints[handle.Index] = value;
    }

    public long this[StateHandle<long> handle]
    {
        get => _longs[handle.Index];
        set => _longs[handle.Index] = value;
    }

    public float this[StateHandle<float> handle]
    {
        get => _floats[handle.Index];
        set => _floats[handle.Index] = value;
    }

    public double this[StateHandle<double> handle]
    {
        get => _doubles[handle.Index];
        set => _doubles[handle.Index] = value;
    }

    public bool this[StateHandle<bool> handle]
    {
        get => _bools[handle.Index];
        set => _bools[handle.Index] = value;
    }

    /// <summary>Reference slots need their own accessor pair, since an indexer cannot be generic.</summary>
    public T? GetRef<T>(StateHandle<T> handle) where T : class => (T?)_refs[handle.Index];

    public void SetRef<T>(StateHandle<T> handle, T? value) where T : class => _refs[handle.Index] = value;

    internal void SetIntRaw(int index, int value) => _ints[index] = value;
    internal void SetFloatRaw(int index, float value) => _floats[index] = value;
    internal void SetDoubleRaw(int index, double value) => _doubles[index] = value;
    internal void SetBoolRaw(int index, bool value) => _bools[index] = value;
}
