using Microsoft.JSInterop;

namespace Finances.App.Client.Services.Storage;

public sealed class LocalStorageKeyValueStorage : IKeyValueStorage
{
    private readonly IJSRuntime _js;

    public LocalStorageKeyValueStorage(IJSRuntime js)
    {
        _js = js;
    }

    public ValueTask<string?> GetItemAsync(string key)
    {
        return _js.InvokeAsync<string?>("localStorage.getItem", key);
    }

    public ValueTask SetItemAsync(string key, string value)
    {
        return _js.InvokeVoidAsync("localStorage.setItem", key, value);
    }

    public ValueTask RemoveItemAsync(string key)
    {
        return _js.InvokeVoidAsync("localStorage.removeItem", key);
    }
}
