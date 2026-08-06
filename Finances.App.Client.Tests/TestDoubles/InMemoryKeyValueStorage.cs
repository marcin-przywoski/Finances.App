using Finances.App.Client.Services.Storage;

namespace Finances.App.Client.Tests.TestDoubles;

public sealed class InMemoryKeyValueStorage : IKeyValueStorage
{
    public Dictionary<string, string> Items { get; } = [];
    public int ReadCount { get; private set; }
    public int WriteCount { get; private set; }

    /// <summary>
    /// When true, every operation yields first, mimicking real JS interop so
    /// tests can exercise interleaved-await races.
    /// </summary>
    public bool SimulateAsync { get; set; }

    public async ValueTask<string?> GetItemAsync(string key)
    {
        if (SimulateAsync)
        {
            await Task.Yield();
        }

        ReadCount++;
        return Items.TryGetValue(key, out var value) ? value : null;
    }

    public async ValueTask SetItemAsync(string key, string value)
    {
        if (SimulateAsync)
        {
            await Task.Yield();
        }

        WriteCount++;
        Items[key] = value;
    }

    public async ValueTask RemoveItemAsync(string key)
    {
        if (SimulateAsync)
        {
            await Task.Yield();
        }

        Items.Remove(key);
    }
}
