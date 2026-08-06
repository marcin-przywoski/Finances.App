using System.Globalization;
using System.Text;
using Finances.App.Shared;

namespace Finances.App.Client.Services;

/// <summary>
/// CSV files for the accountant. The consumer is Excel on the downloader's
/// machine, so the separator and decimal format follow the current culture
/// (Polish Excel expects ';' with comma decimals); dates stay ISO via
/// DateKey. Content starts with a UTF-8 BOM so Excel decodes Polish
/// characters correctly.
/// </summary>
public static class CsvExport
{
    public const string ContentType = "text/csv;charset=utf-8";

    public static string BuildServiceRecordsCsv(IEnumerable<ServiceRecord> records)
    {
        var sb = StartCsv();
        AppendRow(sb, "Date", "Worker", "Service", "Client", "Amount", "Commission %", "Worker share", "Salon share", "Tips", "Notes");

        foreach (var record in records)
        {
            AppendRow(sb,
                record.DatePerformed.ToDateKey(),
                record.Worker?.Name ?? "",
                record.Service?.Name ?? "",
                record.ClientName ?? "",
                Money(record.AmountPaid),
                Money(record.CommissionPercentageApplied),
                Money(record.WorkerShare),
                Money(record.SalonShare),
                Money(record.Tips),
                record.Notes ?? "");
        }

        return sb.ToString();
    }

    public static string BuildProductSalesCsv(IEnumerable<ProductSale> sales)
    {
        var sb = StartCsv();
        AppendRow(sb, "Date", "Product", "Worker", "Client", "Quantity", "Unit price", "Total", "Notes");

        foreach (var sale in sales)
        {
            AppendRow(sb,
                sale.DateSold.ToDateKey(),
                sale.Product?.Name ?? "",
                sale.Worker?.Name ?? "",
                sale.ClientName ?? "",
                sale.Quantity.ToString(CultureInfo.CurrentCulture),
                Money(sale.UnitPrice),
                Money(sale.TotalPrice),
                sale.Notes ?? "");
        }

        return sb.ToString();
    }

    public static string BuildExpensesCsv(IEnumerable<Expense> expenses)
    {
        var sb = StartCsv();
        AppendRow(sb, "Date", "Category", "Amount", "Note");

        foreach (var expense in expenses)
        {
            AppendRow(sb,
                expense.Date.ToDateKey(),
                expense.Category,
                Money(expense.Amount),
                expense.Note ?? "");
        }

        return sb.ToString();
    }

    private static StringBuilder StartCsv()
    {
        // The BOM character survives the string → Blob → file path and is what
        // makes Excel treat the file as UTF-8.
        return new StringBuilder("\uFEFF");
    }

    private static string Separator =>
        string.IsNullOrEmpty(CultureInfo.CurrentCulture.TextInfo.ListSeparator)
            ? ","
            : CultureInfo.CurrentCulture.TextInfo.ListSeparator;

    private static string Money(decimal value)
    {
        return value.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static void AppendRow(StringBuilder sb, params string[] fields)
    {
        var separator = Separator;
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }

            sb.Append(Quote(fields[i], separator));
        }

        sb.Append("\r\n");
    }

    private static string Quote(string field, string separator)
    {
        // RFC 4180: quote when the field contains the separator, a quote, or
        // a line break; escape quotes by doubling them.
        if (field.Contains(separator, StringComparison.Ordinal)
            || field.Contains('"', StringComparison.Ordinal)
            || field.Contains('\n', StringComparison.Ordinal)
            || field.Contains('\r', StringComparison.Ordinal))
        {
            return $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return field;
    }
}
