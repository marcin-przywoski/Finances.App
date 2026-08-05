using System.Globalization;

namespace Finances.App.Client.Services;

/// <summary>
/// Formats money using the currency persisted in the snapshot settings.
/// Without a setting it falls back to the browser culture's currency format,
/// which is the app's original behavior. The currency table is built in
/// rather than looked up through RegionInfo so it behaves identically under
/// trimming and sharded ICU data.
/// </summary>
public sealed class MoneyFormat
{
    public sealed record CurrencyOption(string Code, string Symbol, bool SymbolBefore);

    public static readonly IReadOnlyList<CurrencyOption> SupportedCurrencies =
    [
        new("PLN", "zł", false),
        new("EUR", "€", true),
        new("USD", "$", true),
        new("GBP", "£", true),
        new("CZK", "Kč", false),
        new("UAH", "₴", false)
    ];

    private CurrencyOption? _current;

    public event Action? OnChange;

    public string? CurrencyCode => _current?.Code;

    public static bool IsSupported(string? code)
    {
        return code is not null
            && SupportedCurrencies.Any(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public void SetCurrency(string? code)
    {
        var next = SupportedCurrencies.FirstOrDefault(
            option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));

        if (!Equals(next, _current))
        {
            _current = next;
            OnChange?.Invoke();
        }
    }

    public string Format(decimal amount)
    {
        if (_current is null)
        {
            return amount.ToString("C", CultureInfo.CurrentCulture);
        }

        var number = amount.ToString("N2", CultureInfo.CurrentCulture);
        return _current.SymbolBefore ? $"{_current.Symbol}{number}" : $"{number} {_current.Symbol}";
    }
}
