using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace DTIOneLink.Tests.TestDoubles;

// Minimal in-memory ISession so controller code that reads/writes
// HttpContext.Session (via the SetString/GetString/GetInt32 extensions) can
// run in a unit test without the real session middleware or a backing store.
public class FakeSession : ISession
{
    private readonly Dictionary<string, byte[]> _store = new();

    public bool IsAvailable => true;
    public string Id => "test-session";
    public IEnumerable<string> Keys => _store.Keys;

    public void Clear() => _store.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public void Set(string key, byte[] value) => _store[key] = value;
    public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
}

public class FakeSessionFeature : ISessionFeature
{
    public ISession Session { get; set; } = new FakeSession();
}
