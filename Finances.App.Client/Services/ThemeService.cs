using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

/// <summary>
/// C# side of the theme preference ('light' | 'dark' | 'auto'), stored under
/// the Finances.App.theme localStorage key and applied by js/theme.js. Kept
/// out of the snapshot on purpose: theme is a per-device preference and must
/// be applied before the snapshot could ever be read.
/// </summary>
public sealed class ThemeService
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Auto = "auto";

    private readonly IJSRuntime _js;

    public event Action? OnChange;

    public string Preference { get; private set; } = Auto;

    public ThemeService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        try
        {
            Preference = await _js.InvokeAsync<string>("financeTheme.get");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Theme init failed: {ex.Message}");
        }
    }

    /// <summary>Cycles auto → dark → light → auto.</summary>
    public Task CycleAsync()
    {
        var next = Preference switch
        {
            Auto => Dark,
            Dark => Light,
            _ => Auto
        };
        return SetAsync(next);
    }

    public async Task SetAsync(string preference)
    {
        Preference = preference;
        try
        {
            await _js.InvokeVoidAsync("financeTheme.set", preference);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Theme change failed: {ex.Message}");
        }

        OnChange?.Invoke();
    }
}
