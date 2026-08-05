using System.Globalization;
using Finances.App.Client.Services;
using Finances.App.Shared;

namespace Finances.App.Client.Tests;

/// <summary>
/// CSV output contract: culture-aware separator and decimals (Polish Excel
/// expects ';' with comma decimals), ISO dates, UTF-8 BOM, RFC 4180 quoting.
/// </summary>
public class CsvExportTests
{
    private static T WithCulture<T>(string cultureName, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static readonly Expense SampleExpense = new()
    {
        Id = 1,
        Date = new DateTime(2026, 8, 5),
        Category = "Czynsz łóżko",
        Amount = 1234.56m,
        Note = "August"
    };

    [Fact]
    public void Polish_culture_uses_semicolons_and_comma_decimals()
    {
        var csv = WithCulture("pl-PL", () => CsvExport.BuildExpensesCsv([SampleExpense]));

        Assert.Contains("2026-08-05;Czynsz łóżko;1234,56;August", csv);
    }

    [Fact]
    public void Us_culture_uses_commas_and_dot_decimals()
    {
        var csv = WithCulture("en-US", () => CsvExport.BuildExpensesCsv([SampleExpense]));

        Assert.Contains("2026-08-05,Czynsz łóżko,1234.56,August", csv);
    }

    [Fact]
    public void Output_starts_with_utf8_bom_for_excel()
    {
        var csv = WithCulture("pl-PL", () => CsvExport.BuildExpensesCsv([]));

        Assert.Equal('﻿', csv[0]);
    }

    [Fact]
    public void Fields_containing_separator_quote_or_newline_are_quoted()
    {
        var expense = new Expense
        {
            Id = 1,
            Date = new DateTime(2026, 8, 5),
            Category = "Supplies",
            Amount = 10m,
            Note = "line1\nsays \"hi\", ok"
        };

        var csv = WithCulture("en-US", () => CsvExport.BuildExpensesCsv([expense]));

        Assert.Contains("\"line1\nsays \"\"hi\"\", ok\"", csv);
    }

    [Fact]
    public void Service_records_include_shares_and_names()
    {
        var record = new ServiceRecord
        {
            Id = 1,
            WorkerId = 1,
            Worker = new Worker { Id = 1, Name = "Jan" },
            ServiceId = 1,
            Service = new Service { Id = 1, Name = "Cut" },
            DatePerformed = new DateTime(2026, 8, 5),
            AmountPaid = 100m,
            CommissionPercentageApplied = 50m,
            Tips = 10m
        };

        var csv = WithCulture("en-US", () => CsvExport.BuildServiceRecordsCsv([record]));

        Assert.Contains("Date,Worker,Service,Client,Amount,Commission %,Worker share,Salon share,Tips,Notes", csv);
        Assert.Contains("2026-08-05,Jan,Cut,,100.00,50.00,50.00,50.00,10.00,", csv);
    }

    [Fact]
    public void Product_sales_include_totals()
    {
        var sale = new ProductSale
        {
            Id = 1,
            ProductId = 1,
            Product = new Product { Id = 1, Name = "Pomade" },
            DateSold = new DateTime(2026, 8, 5),
            Quantity = 3,
            UnitPrice = 25m
        };

        var csv = WithCulture("en-US", () => CsvExport.BuildProductSalesCsv([sale]));

        Assert.Contains("2026-08-05,Pomade,,,3,25.00,75.00,", csv);
    }
}
