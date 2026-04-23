using Finances.App.Client.Services;
using Xunit;

namespace Finances.App.Tests;

public class InvoiceParserServiceTests
{
    [Fact]
    public void Parse_EmptyText_ReturnsAllNulls()
    {
        var result = InvoiceParserService.Parse(string.Empty);

        Assert.Null(result.Vendor);
        Assert.Null(result.Date);
        Assert.Null(result.Total);
        Assert.Null(result.Currency);
    }

    [Fact]
    public void Parse_PolishStyleInvoice_ExtractsVendorDateTotalAndCurrency()
    {
        // Representative text pulled from Polish supplier invoices. Commas act
        // as decimal separators, spaces as thousands separators, and the total
        // is labelled "Razem do zapłaty".
        const string text = """
            Hurtownia Kosmetyczna Sp. z o.o.
            ul. Długa 12, 00-001 Warszawa
            NIP 123-456-78-90

            Faktura VAT nr FV/2026/04/17
            Data wystawienia: 17.04.2026

            Pozycja                 Ilość   Cena        Wartość
            Szampon 500ml           3       45,00       135,00
            Odżywka 500ml           2       60,00       120,00

            Suma netto                                   255,00
            VAT 23%                                       58,65
            Razem do zapłaty                             313,65 PLN
            """;

        var result = InvoiceParserService.Parse(text);

        Assert.Equal(new DateTime(2026, 4, 17), result.Date);
        Assert.Equal(313.65m, result.Total);
        Assert.Equal("PLN", result.Currency);
        Assert.Equal("Hurtownia Kosmetyczna Sp. z o.o.", result.Vendor);
    }

    [Fact]
    public void Parse_EnglishStyleInvoice_RecognisesTotalAndEur()
    {
        const string text = """
            Beauty Supply Co.
            Invoice #INV-4421
            Issued: 2026-04-02

            Item                    Qty    Unit      Line
            Scissors                1      89.00     89.00
            Blow dryer              1      199.00    199.00

            Subtotal                                 288.00
            VAT (23%)                                 66.24
            Grand Total                              354.24 EUR
            """;

        var result = InvoiceParserService.Parse(text);

        Assert.Equal(new DateTime(2026, 4, 2), result.Date);
        Assert.Equal(354.24m, result.Total);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal("Beauty Supply Co.", result.Vendor);
    }

    [Fact]
    public void Parse_UsesHighestMoney_WhenNoTotalKeyword()
    {
        // No "total" keyword — parser should fall back to the largest money
        // figure as the best guess, which is the true grand total 499.00.
        const string text = """
            Studio Fryzjerski "Loki"
            15/03/2026

            Cena:      120,00
            Opłata:    379,00
            Wartość:   499,00 zł
            """;

        var result = InvoiceParserService.Parse(text);

        Assert.Equal(new DateTime(2026, 3, 15), result.Date);
        Assert.Equal(499.00m, result.Total);
        Assert.Equal("PLN", result.Currency);
    }

    [Fact]
    public void Parse_IgnoresInvalidDatesAndOutOfRangeYears()
    {
        const string text = """
            Vendor Name
            Reference ID: 1999-13-40
            Date: 2026-05-10
            Total: 50.00 USD
            """;

        var result = InvoiceParserService.Parse(text);

        Assert.Equal(new DateTime(2026, 5, 10), result.Date);
        Assert.Equal(50.00m, result.Total);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void IsPdfMime_AcceptsPdfOnly()
    {
        Assert.True(InvoiceParserService.IsPdfMime("application/pdf"));
        Assert.True(InvoiceParserService.IsPdfMime("APPLICATION/PDF"));
        Assert.False(InvoiceParserService.IsPdfMime("image/png"));
        Assert.False(InvoiceParserService.IsPdfMime(null));
        Assert.False(InvoiceParserService.IsPdfMime(string.Empty));
    }
}
