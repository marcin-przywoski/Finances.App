using Finances.App.Client.Services.Storage;

namespace Finances.App.Client.Tests.TestDoubles;

/// <summary>
/// Wraps an inner storage and can be switched to fail writes, simulating a
/// full or unavailable localStorage.
/// </summary>
public sealed class ThrowingKeyValueStorage : IKeyValueStorage
{
    private readonly InMemoryKeyValueStorage _inner = new();

    public bool FailWrites { get; set; }
    public Exception WriteException { get; set; } = new InvalidOperationException("QuotaExceededError: storage is full.");

    public Dictionary<string, string> Items => _inner.Items;

    public ValueTask<string?> GetItemAsync(string key)
    {
        return _inner.GetItemAsync(key);
    }

    public ValueTask SetItemAsync(string key, string value)
    {
        if (FailWrites)
        {
            throw WriteException;
        }

        return _inner.SetItemAsync(key, value);
    }

    public ValueTask RemoveItemAsync(string key)
    {
        return _inner.RemoveItemAsync(key);
    }
}
