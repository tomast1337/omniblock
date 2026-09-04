namespace OmniBlock.Util.Maths;

public sealed class WeightedRandomSelector<T>
{
    private readonly List<int> _cumulativeWeight = [0];
    private readonly List<T> _items = [];

    public bool Empty => _items.Count == 0;

    /// <summary>Items with their individual (non-cumulative) weights, in insertion order.</summary>
    internal IEnumerable<(T Item, int Weight)> Entries =>
        _items.Select((item, i) => (item, _cumulativeWeight[i + 1] - _cumulativeWeight[i]));

    public void Add(T item, int weight)
    {
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be positive.");

        _items.Add(item);
        _cumulativeWeight.Add(_cumulativeWeight.Last() + weight);
    }

    // Note: You might want to ensure that it's not empty before calling this method, otherwise it will throw an exception.
    public T GetNext(JavaRandom random) => GetNext(random.NextInt(_cumulativeWeight.Last()));

    private T GetNext(int r)
    {
        if (Empty) throw new InvalidOperationException("No items to select from.");

        var index = _cumulativeWeight.BinarySearch(r);
        if (index < 0) index = ~index - 1; // If not found, BinarySearch returns the bitwise complement of the index of the next element that is larger than the search value.

        return _items[index];
    }

    public void Clear()
    {
        _items.Clear();
        _cumulativeWeight.Clear();
        _cumulativeWeight.Add(0);
    }
}
