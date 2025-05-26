namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Shared.Models;
using ImageMagick;
using Tesseract;
using System.Text.RegularExpressions;
using KubiczPlace.Finances.Shared.Services;

public class TesseractInvoiceProcessor : IInvoiceProcessor
{
    public async Task ProcessAsync(Invoice invoice, CancellationToken ct = default)
    {
        // Convert PDF pages to images and OCR each
        using var collection = new MagickImageCollection();
        collection.Read(invoice.Content, new MagickReadSettings { Density = new Density(300) });

        using var engine = new TesseractEngine("./tessdata", "eng", EngineMode.Default);
        var sb = new System.Text.StringBuilder();

        foreach (var pageImg in collection)
        {
            using var img = pageImg.Clone();
            img.Format = MagickFormat.Png;
            var pngBytes = img.ToByteArray();
            using var pix = Pix.LoadFromMemory(pngBytes);
            using var tPage = engine.Process(pix);
            sb.AppendLine(tPage.GetText());
        }

        var text = sb.ToString();
        invoice.OcrText = text;

        // regex extraction across combined text
        var numberMatch = Regex.Match(text, @"Invoice\s*(No\.|#)?\s*([A-Z0-9-]+)", RegexOptions.IgnoreCase);
        if (numberMatch.Success)
            invoice.Number = numberMatch.Groups[2].Value;

        var amountMatch = Regex.Match(text, @"Total\s+([0-9]+[\.,][0-9]{2})", RegexOptions.IgnoreCase);
        if (amountMatch.Success && decimal.TryParse(amountMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amt))
            invoice.Amount = amt;

        var payerMatch = Regex.Match(text, @"Paid\s+by\s*:?\s*(.+)", RegexOptions.IgnoreCase);
        if (payerMatch.Success)
            invoice.Payer = payerMatch.Groups[1].Value.Trim();
    }
}
