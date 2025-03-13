using System.Diagnostics.CodeAnalysis;

namespace ClientCertEchoClient.Helpers;

// This is a simple LRU cache implementation using a linked list and a dictionary
public class LruCache<TKey, TValue>(int capacity, bool ownsValues = false) : IDisposable
    where TKey : notnull
    where TValue : notnull
{
    private bool DisposeOnRemoved { get; } = ownsValues && typeof(IDisposable).IsAssignableFrom(typeof(TValue));
    private readonly int _capacity = capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _cacheMap = new(capacity);
    private readonly LinkedList<(TKey Key, TValue Value)> _lruList = new();

    public void Add(TKey key, TValue value)
    {
        if (_cacheMap.TryGetValue(key, out var node))
        {
            RemoveNode(node);
        }
        else if (_cacheMap.Count >= _capacity)
        {
            RemoveLru();
        }

        var newNode = new LinkedListNode<(TKey Key, TValue Value)>((key, value));
        _lruList.AddFirst(newNode);
        _cacheMap[key] = newNode;
    }

    public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        if (_cacheMap.TryGetValue(key, out var node))
        {
            _lruList.Remove(node);
            _lruList.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default;
        return false;
    }

    public void RemoveAll(Func<TValue, bool> predicate)
    {
        if (_cacheMap.Count == 0)
        {
            return;
        }

        var node = _lruList.First;
        while (node != null)
        {
            var nextNode = node.Next;
            if (predicate(node.Value.Value))
            {
                RemoveNode(node);
            }
            node = nextNode;
        };
    }

    private void RemoveNode(LinkedListNode<(TKey Key, TValue Value)> node)
    {
        _lruList.Remove(node);
        _cacheMap.Remove(node.Value.Key);
        if (DisposeOnRemoved)
        {
            ((IDisposable)node.Value.Value).Dispose();
        }
    }

    private void RemoveLru()
    {
        var lru = _lruList.Last!;
        _lruList.RemoveLast();
        _cacheMap.Remove(lru.Value.Key);
        if (DisposeOnRemoved)
        {
            ((IDisposable)lru.Value.Value).Dispose();
        }
    }

    public void RemoveAll()
    {
        (TKey, TValue)[]? toDispose = DisposeOnRemoved ? [.. _lruList] : [];

        _lruList.Clear();
        _cacheMap.Clear();

        foreach (var (_, Value) in toDispose)
        {
            ((IDisposable)Value).Dispose();
        }
    }

    public void Dispose() => RemoveAll();
}
