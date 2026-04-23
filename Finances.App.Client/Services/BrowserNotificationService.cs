using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

public enum BrowserNotificationPermission
{
    Unsupported,
    Default,
    Granted,
    Denied
}

/// <summary>
/// Wraps the browser Notification API through <c>wwwroot/js/notifications.js</c>.
/// User opt-in is cached in localStorage so the app respects an explicit "do not ask again" choice.
/// </summary>
public sealed class BrowserNotificationService
{
    private const string EnabledKey = "Finances.App.notifications.enabled";
    private const string PromptDismissedKey = "Finances.App.notifications.promptDismissed";
    private readonly IJSRuntime _js;

    public BrowserNotificationService(IJSRuntime js)
    {
        _js = js;
    }

    public event Action? OnChange;

    public async Task<BrowserNotificationPermission> GetPermissionAsync()
    {
        try
        {
            var state = await _js.InvokeAsync<string>("financeNotifications.getPermission");
            return ParseState(state);
        }
        catch
        {
            return BrowserNotificationPermission.Unsupported;
        }
    }

    public async Task<BrowserNotificationPermission> RequestPermissionAsync()
    {
        try
        {
            var state = await _js.InvokeAsync<string>("financeNotifications.requestPermission");
            var parsed = ParseState(state);
            if (parsed == BrowserNotificationPermission.Granted)
            {
                await SetEnabledAsync(true);
            }
            OnChange?.Invoke();
            return parsed;
        }
        catch
        {
            return BrowserNotificationPermission.Unsupported;
        }
    }

    public async Task<bool> IsEnabledAsync()
    {
        var permission = await GetPermissionAsync();
        if (permission != BrowserNotificationPermission.Granted) return false;
        var pref = await _js.InvokeAsync<string?>("localStorage.getItem", EnabledKey);
        return pref is null || pref == "true";
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        await _js.InvokeVoidAsync("localStorage.setItem", EnabledKey, enabled ? "true" : "false");
        OnChange?.Invoke();
    }

    public async Task<bool> IsPromptDismissedAsync()
    {
        var value = await _js.InvokeAsync<string?>("localStorage.getItem", PromptDismissedKey);
        return value == "true";
    }

    public async Task DismissPromptAsync()
    {
        await _js.InvokeVoidAsync("localStorage.setItem", PromptDismissedKey, "true");
        OnChange?.Invoke();
    }

    public async Task<bool> NotifyAsync(string tag, string title, string? body = null, string? url = null)
    {
        if (!await IsEnabledAsync()) return false;
        try
        {
            return await _js.InvokeAsync<bool>("financeNotifications.show", tag, title, body, url);
        }
        catch
        {
            return false;
        }
    }

    private static BrowserNotificationPermission ParseState(string? state)
    {
        return state switch
        {
            "granted" => BrowserNotificationPermission.Granted,
            "denied" => BrowserNotificationPermission.Denied,
            "default" => BrowserNotificationPermission.Default,
            _ => BrowserNotificationPermission.Unsupported
        };
    }
}
