using Microsoft.JSInterop;

namespace Finances.App.Tests;

/// <summary>
/// Minimal in-memory stand-in for <see cref="IJSRuntime"/> used by the Blazor
/// WebAssembly client. Handles only the localStorage calls made by
/// <c>LocalFinanceStore</c>; everything else is answered with the default value
/// for the requested type so optional call sites can be exercised without
/// additional wiring.
/// </summary>
internal sealed class FakeJSRuntime : IJSRuntime
{
    private readonly Dictionary<string, string?> _localStorage = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string?> LocalStorage => _localStorage;

    public string? GetItem(string key) =>
        _localStorage.TryGetValue(key, out var value) ? value : null;

    public void SetItem(string key, string value) => _localStorage[key] = value;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        object? result = identifier switch
        {
            "localStorage.getItem" => HandleGetItem(args),
            "localStorage.setItem" => HandleSetItem(args),
            "localStorage.removeItem" => HandleRemoveItem(args),
            _ => null
        };

        var typed = result is null ? default(TValue)! : (TValue)result;
        return ValueTask.FromResult(typed);
    }

    private string? HandleGetItem(object?[]? args)
    {
        if (args is null || args.Length == 0) return null;
        var key = args[0] as string;
        return key is null ? null : GetItem(key);
    }

    private object? HandleSetItem(object?[]? args)
    {
        if (args is null || args.Length < 2) return null;
        var key = args[0] as string;
        var value = args[1] as string;
        if (key is not null)
        {
            _localStorage[key] = value;
        }
        return null;
    }

    private object? HandleRemoveItem(object?[]? args)
    {
        if (args is null || args.Length == 0) return null;
        var key = args[0] as string;
        if (key is not null)
        {
            _localStorage.Remove(key);
        }
        return null;
    }
}
