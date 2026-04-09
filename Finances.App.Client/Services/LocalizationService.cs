using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

public sealed class LocalizationService
{
    private const string StorageKey = "Finances.App.locale";
    private const string DefaultLocale = "en-US";
    private static readonly string[] SupportedLocales = ["en-US", "pl-PL"];

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    private Dictionary<string, string> _strings = new();
    private string _currentLocale = DefaultLocale;
    private bool _initialized;

    public event Action? OnChange;

    public string CurrentLocale => _currentLocale;
    public IReadOnlyList<string> AvailableLocales => SupportedLocales;

    public LocalizationService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public string this[string key] => _strings.GetValueOrDefault(key, key);

    public string T(string key) => _strings.GetValueOrDefault(key, key);

    public string T(string key, params object[] args)
    {
        var template = _strings.GetValueOrDefault(key, key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            var saved = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(saved) && SupportedLocales.Contains(saved))
                _currentLocale = saved;
        }
        catch { }

        await LoadStringsAsync();
        _initialized = true;
    }

    public async Task SetLocaleAsync(string locale)
    {
        if (!SupportedLocales.Contains(locale) || locale == _currentLocale)
            return;

        _currentLocale = locale;

        try { await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, locale); }
        catch { }

        await LoadStringsAsync();
        OnChange?.Invoke();
    }

    private async Task LoadStringsAsync()
    {
        try
        {
            var data = await _http.GetFromJsonAsync<Dictionary<string, string>>($"locales/{_currentLocale}.json");
            _strings = data ?? new();
        }
        catch
        {
            _strings = new();
        }
    }

    public string GetLocaleDisplayName(string locale) => locale switch
    {
        "en-US" => "English",
        "pl-PL" => "Polski",
        _ => locale
    };
}
