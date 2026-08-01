using System.Collections;
using System.Collections.Immutable;

namespace BetaSharp.Generators;

/// <summary>
///     An <see cref="ImmutableArray{T}" /> that compares by contents.
///     <para>
///         The incremental pipeline caches on model equality, and <see cref="ImmutableArray{T}" />
///         compares by reference — a model holding one would be unequal to itself on every
///         keystroke, defeating the caching the pipeline exists for.
///     </para>
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> values)
    : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _values = values;

    public int Count => _values.IsDefault ? 0 : _values.Length;

    public T this[int index] => _values[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_values.IsDefault || other._values.IsDefault)
        {
            return _values.IsDefault && other._values.IsDefault;
        }

        if (_values.Length != other._values.Length)
        {
            return false;
        }

        for (int i = 0; i < _values.Length; i++)
        {
            if (!_values[i].Equals(other._values[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_values.IsDefault)
        {
            return 0;
        }

        int hash = 17;
        foreach (T value in _values)
        {
            hash = (hash * 31) + value.GetHashCode();
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() =>
        (_values.IsDefault ? ImmutableArray<T>.Empty : _values).AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
