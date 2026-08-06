namespace Finances.App.Client.Services.Storage;

/// <summary>
/// Abstraction over browser localStorage so services that persist data
/// can be unit tested without a JavaScript runtime.
/// </summary>
public interface IKeyValueStorage
{
    ValueTask<string?> GetItemAsync(string key);
    ValueTask SetItemAsync(string key, string value);
    ValueTask RemoveItemAsync(string key);
}
