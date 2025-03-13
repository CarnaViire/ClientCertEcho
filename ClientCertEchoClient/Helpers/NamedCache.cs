namespace ClientCertEchoClient.Helpers;

// Simple cache class to hold the instances and their creation time
// The cache is cleaned up periodically based on the lifetime
// The cache is thread-safe and uses a simple LRU strategy to limit the number of cached items
public class NamedCache<T>(Func<string, IServiceProvider, T> factory, TimeSpan ttl) : IDisposable
{
    private const int DefaultCapacity = 10; // For testing only

    private readonly Func<string, IServiceProvider, T> _factory = factory;
    private readonly LruCache<string, Entry> _cache = new(DefaultCapacity, ownsValues: true);
    private readonly TimeSpan _ttl = ttl;
    private DateTime _lastCleanup = DateTime.UtcNow;

    public T GetOrCreate(string clientName, IServiceProvider services)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastCleanup > _ttl)
        {
            lock (_cache)
            {
                _cache.RemoveAll(entry => (now - entry.CreatedAt) > _ttl);
            }
            _lastCleanup = now;
        }

        lock (_cache)
        {
            if (!_cache.TryGetValue(clientName, out var entry))
            {
                entry = new Entry(_factory(clientName, services), now);
                _cache.Add(clientName, entry);
            }
            return entry.Item;
        }
    }

    public void Dispose()
    {
        lock (_cache)
        {
            _cache.Dispose();
        }
    }

    private class Entry(T item, DateTime createdAt) : IDisposable
    {
        public T Item => item;
        public DateTime CreatedAt => createdAt;

        public void Dispose()
        {
            (Item as IDisposable)?.Dispose();
        }
    }
}
