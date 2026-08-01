using Finances.App.Client.Services.Storage;

namespace Finances.App.Client.Tests.TestDoubles;

public sealed class InMemoryKeyValueStorage : IKeyValueStorage
{
    public Dictionary<string, string> Items { get; } = [];
    public int ReadCount { get; private set; }
    public int WriteCount { get; private set; }

    public ValueTask<string?> GetItemAsync(string key)
    {
        ReadCount++;
        return ValueTask.FromResult(Items.TryGetValue(key, out var value) ? value : null);
    }

    public ValueTask SetItemAsync(string key, string value)
    {
        WriteCount++;
        Items[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveItemAsync(string key)
    {
        Items.Remove(key);
        return ValueTask.CompletedTask;
    }
}
