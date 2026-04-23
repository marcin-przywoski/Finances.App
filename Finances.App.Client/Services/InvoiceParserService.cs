using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

/// <summary>
/// Best-effort suggestions extracted from an uploaded PDF. Any field may be
/// null when the heuristics could not identify it; callers should treat this
/// as hints to pre-fill the form, not as authoritative data.
/// </summary>
public sealed record InvoiceParseResult(
    string? Vendor,
    DateTime? Date,
    decimal? Total,
    string? Currency,
    int PageCount);

/// <summary>
/// Lightweight pipeline that turns an uploaded supplier invoice (PDF only for
/// now) into structured hints. Extraction runs in the browser via PDF.js; all
/// parsing is pure C# and fully unit-testable through <see cref="Parse(string, int)"/>.
/// Image OCR is intentionally out of scope for v1 — scanned invoices fall
/// through to manual entry without any error surfaced to the user.
/// </summary>
public sealed class InvoiceParserService
{
    private readonly IJSRuntime _js;

    public InvoiceParserService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Attempt to parse a PDF attachment. Returns <c>null</c> when the browser
    /// could not load PDF.js (offline), the file is not a PDF, or no text
    /// could be extracted (image-only scan, encrypted document).
    /// </summary>
    public async Task<InvoiceParseResult?> TryParsePdfAsync(byte[] content, string? mime)
    {
        if (content is null || content.Length == 0) return null;
        if (!IsPdfMime(mime)) return null;

        var base64 = Convert.ToBase64String(content);
        JsExtractResult? dto;
        try
        {
            dto = await _js.InvokeAsync<JsExtractResult?>("financePdfExtract.extractText", base64, 5);
        }
        catch
        {
            return null;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Text)) return null;
        return Parse(dto.Text, dto.PageCount);
    }

    public static bool IsPdfMime(string? mime) =>
        !string.IsNullOrWhiteSpace(mime) &&
        mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pure parsing entry point. Exposed for unit tests and optional reuse by
    /// future pipelines (e.g. OCR output).
    /// </summary>
    public static InvoiceParseResult Parse(string rawText, int pageCount = 1)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new InvoiceParseResult(null, null, null, null, pageCount);
        }

        var vendor = ExtractVendor(rawText);
        var date = ExtractDate(rawText);
        var (total, currency) = ExtractTotalAndCurrency(rawText);

        return new InvoiceParseResult(vendor, date, total, currency, pageCount);
    }

    // ── Date extraction ─────────────────────────────────────────
    // Recognises 2026-04-15, 15.04.2026, 15/04/2026 and common separators.
    private static readonly Regex DateRegex = new(
        @"\b(?:(?<y1>\d{4})[-./](?<m1>\d{1,2})[-./](?<d1>\d{1,2})|(?<d2>\d{1,2})[-./](?<m2>\d{1,2})[-./](?<y2>\d{4}))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static DateTime? ExtractDate(string text)
    {
        var currentYear = DateTime.Today.Year;
        foreach (Match m in DateRegex.Matches(text))
        {
            int y, mo, d;
            if (m.Groups["y1"].Success)
            {
                y = int.Parse(m.Groups["y1"].Value, CultureInfo.InvariantCulture);
                mo = int.Parse(m.Groups["m1"].Value, CultureInfo.InvariantCulture);
                d = int.Parse(m.Groups["d1"].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                d = int.Parse(m.Groups["d2"].Value, CultureInfo.InvariantCulture);
                mo = int.Parse(m.Groups["m2"].Value, CultureInfo.InvariantCulture);
                y = int.Parse(m.Groups["y2"].Value, CultureInfo.InvariantCulture);
            }

            if (y < 2000 || y > currentYear + 1) continue;
            if (mo < 1 || mo > 12) continue;
            if (d < 1 || d > 31) continue;

            try
            {
                return new DateTime(y, mo, d);
            }
            catch (ArgumentOutOfRangeException)
            {
                // invalid day for month — keep looking
            }
        }

        return null;
    }

    // ── Money + currency extraction ─────────────────────────────
    // Captures numbers with either comma or dot decimals, and thousands
    // separators (space, nbsp, dot, comma). Currency trails or is omitted.
    private static readonly Regex MoneyRegex = new(
        @"(?<amount>\d{1,3}(?:[ \u00A0.,]\d{3})*(?:[.,]\d{2})|\d+[.,]\d{2})\s*(?<currency>PLN|zł|EUR|USD|€|\$)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Keywords that commonly precede the invoice grand total (Polish + English).
    private static readonly string[] TotalKeywords =
    [
        "do zapłaty",
        "razem do zapłaty",
        "łącznie",
        "suma brutto",
        "razem",
        "suma",
        "grand total",
        "amount due",
        "total due",
        "total"
    ];

    private static (decimal? Total, string? Currency) ExtractTotalAndCurrency(string text)
    {
        var lowered = text.ToLowerInvariant();

        foreach (var keyword in TotalKeywords)
        {
            var idx = lowered.IndexOf(keyword, StringComparison.Ordinal);
            while (idx >= 0)
            {
                var windowStart = idx + keyword.Length;
                if (windowStart >= text.Length) break;

                var windowLength = Math.Min(80, text.Length - windowStart);
                var window = text.Substring(windowStart, windowLength);
                var match = MoneyRegex.Match(window);

                if (match.Success && TryParseMoney(match.Groups["amount"].Value, out var parsed))
                {
                    return (parsed, NormalizeCurrency(match.Groups["currency"].Value));
                }

                idx = lowered.IndexOf(keyword, windowStart, StringComparison.Ordinal);
            }
        }

        // Fall back to the largest money-like number in the document. This is a
        // reasonable heuristic because invoice totals are nearly always the
        // biggest figure on the page (larger than unit prices or line totals).
        decimal highest = 0m;
        string? highestCurrency = null;

        foreach (Match m in MoneyRegex.Matches(text))
        {
            if (!TryParseMoney(m.Groups["amount"].Value, out var parsed)) continue;
            if (parsed <= highest) continue;
            highest = parsed;
            highestCurrency = NormalizeCurrency(m.Groups["currency"].Value);
        }

        return highest > 0m ? (highest, highestCurrency) : (null, null);
    }

    private static bool TryParseMoney(string raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Strip thousands separators. We infer the decimal separator as the
        // last comma-or-dot so "1.234,56" and "1,234.56" both work.
        var cleaned = raw.Replace(" ", "").Replace("\u00A0", "");
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        string normalized;
        if (lastComma >= 0 && lastComma > lastDot)
        {
            normalized = cleaned.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            normalized = cleaned.Replace(",", string.Empty);
        }
        else
        {
            normalized = cleaned;
        }

        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static string? NormalizeCurrency(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        return token.Trim().ToUpperInvariant() switch
        {
            "PLN" or "ZŁ" or "ZL" => "PLN",
            "EUR" or "€" => "EUR",
            "USD" or "$" => "USD",
            _ => null
        };
    }

    // ── Vendor extraction ───────────────────────────────────────
    // Pick the first "meaningful" line — skip headers like "Invoice" / "Faktura"
    // that are almost never the vendor name.
    private static readonly string[] VendorBlocklist =
    [
        "faktura",
        "invoice",
        "rachunek",
        "paragon",
        "receipt",
        "duplikat"
    ];

    private static string? ExtractVendor(string text)
    {
        foreach (var rawLine in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length < 3 || line.Length > 80) continue;
            if (!HasEnoughLetters(line)) continue;

            var lower = line.ToLowerInvariant();
            if (VendorBlocklist.Any(keyword => lower.StartsWith(keyword, StringComparison.Ordinal))) continue;

            return line;
        }

        return null;
    }

    private static bool HasEnoughLetters(string value)
    {
        var letters = 0;
        foreach (var c in value)
        {
            if (char.IsLetter(c)) letters++;
            if (letters >= 3) return true;
        }
        return false;
    }

    private sealed class JsExtractResult
    {
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
        [JsonPropertyName("pageCount")] public int PageCount { get; set; }
    }
}
