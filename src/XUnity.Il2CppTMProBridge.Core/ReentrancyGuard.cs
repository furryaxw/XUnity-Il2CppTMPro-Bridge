namespace XUnity.Il2CppTMProBridge.Core;

public sealed class ReentrancyGuard<TKey> : IDisposable where TKey : notnull
{
    private readonly ThreadLocal<HashSet<TKey>> _active = new(() => new HashSet<TKey>());

    public bool TryEnter(TKey key, out IDisposable? lease)
    {
        var active = _active.Value!;
        if (!active.Add(key))
        {
            lease = null;
            return false;
        }

        lease = new Lease(active, key);
        return true;
    }

    public void Dispose() => _active.Dispose();

    private sealed class Lease : IDisposable
    {
        private HashSet<TKey>? _active;
        private readonly TKey _key;

        public Lease(HashSet<TKey> active, TKey key)
        {
            _active = active;
            _key = key;
        }

        public void Dispose()
        {
            _active?.Remove(_key);
            _active = null;
        }
    }
}
