using System.Collections.Concurrent;

namespace OmniBlock.Util;

public class ObjectPool<T> : IDisposable where T : class
{
    private readonly int capacity;
    private readonly Func<T> factory;
    private readonly ConcurrentBag<T> pool;

    public ObjectPool(Func<T> factory, int capacity = 32)
    {
        this.factory = factory;
        this.capacity = capacity;
        pool = new ConcurrentBag<T>();
    }

    public void Dispose()
    {
        while (pool.TryTake(out var obj))
        {
            if (obj is IDisposable d) d.Dispose();
        }
    }

    public T Get() => pool.TryTake(out var item) ? item : factory();

    public void Return(T obj)
    {
        if (pool.Count < capacity)
        {
            pool.Add(obj);
        }
        else if (obj is IDisposable d)
        {
            d.Dispose();
        }
    }
}
