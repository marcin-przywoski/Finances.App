using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

public sealed class ThemeService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private bool _isDark;
    private bool _initialized;

    public ThemeService(IJSRuntime js)
    {
        _js = js;
    }

    public bool IsDark => _isDark;

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var stored = await _js.InvokeAsync<string?>("localStorage.getItem", "theme");
        if (stored is "dark" or "light")
        {
            _isDark = stored == "dark";
        }
        else
        {
            _isDark = await _js.InvokeAsync<bool>("eval", "matchMedia('(prefers-color-scheme: dark)').matches");
        }

        await ApplyThemeAsync();
    }

    public async Task ToggleAsync()
    {
        _isDark = !_isDark;
        await _js.InvokeVoidAsync("localStorage.setItem", "theme", _isDark ? "dark" : "light");
        await ApplyThemeAsync();
        OnChange?.Invoke();
    }

    private async Task ApplyThemeAsync()
    {
        await _js.InvokeVoidAsync("eval", $"document.documentElement.setAttribute('data-theme', '{(_isDark ? "dark" : "light")}')");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
