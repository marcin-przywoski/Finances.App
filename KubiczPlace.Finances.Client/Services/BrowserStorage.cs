using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace KubiczPlace.Finances.Client.Services;

/// <summary>
/// Minimal helper around browser <c>localStorage</c> without external packages.
/// Stores JSON-serialized data under a string key.
/// </summary>
public sealed class BrowserStorage
{
    private readonly IJSRuntime _js;

    public BrowserStorage(IJSRuntime js) => _js = js;

    public async ValueTask SetAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        await _js.InvokeVoidAsync("localStorage.setItem", key, json);
    }

    public async ValueTask<T?> GetAsync<T>(string key)
    {
        var json = await _js.InvokeAsync<string?>("localStorage.getItem", key);
        return string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json);
    }

    public async ValueTask RemoveAsync(string key) =>
        await _js.InvokeVoidAsync("localStorage.removeItem", key);
}
