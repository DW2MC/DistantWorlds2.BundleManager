using System.Collections;
using System.Collections.ObjectModel;

namespace DistantWorlds2.BundleManager;

public class LinearSearchReadOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
{
    private readonly KeyValuePair<TKey, TValue>[] _kvs;
    public LinearSearchReadOnlyDictionary(KeyValuePair<TKey, TValue>[] kvs)
        => _kvs = kvs;

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        => _kvs.AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _kvs.GetEnumerator();

    public void Add(KeyValuePair<TKey, TValue> item)
        => throw new NotSupportedException();

    public void Clear()
        => throw new NotSupportedException();

    public bool Contains(KeyValuePair<TKey, TValue> item)
        => _kvs.Contains(item);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        => _kvs.CopyTo(array, arrayIndex);

    public bool Remove(KeyValuePair<TKey, TValue> item)
        => throw new NotSupportedException();

    public int Count => _kvs.Length;

    public bool IsReadOnly => true;

    public bool ContainsKey(TKey key)
        => _kvs.Any(kv => kv.Key!.Equals(key));

    public void Add(TKey key, TValue value)
        => throw new NotSupportedException();

    public bool Remove(TKey key)
        => throw new NotSupportedException();

    public bool TryGetValue(TKey key, out TValue value)
        => !(value = this[key]!).Equals(default);

    public TValue this[TKey key]
    {
        get => _kvs.FirstOrDefault(kv => kv.Key!.Equals(key)).Value;
        set => throw new NotSupportedException();
    }

    public ICollection<TKey> Keys
        => new ReadOnlyCollection<TKey>
        (_kvs.Select(kvp => kvp.Key)
            .ToArray());

    public ICollection<TValue> Values
        => new ReadOnlyCollection<TValue>
        (_kvs.Select(kvp => kvp.Value)
            .ToArray());
}
