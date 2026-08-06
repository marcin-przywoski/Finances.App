using System.Globalization;
using System.Text.Json;
using Finances.App.Client.Services;
using Finances.App.Client.Tests.TestDoubles;
using Finances.App.Shared;

namespace Finances.App.Client.Tests;

/// <summary>
/// Schema v2: expenses, persisted settings, and the v1-to-v2 migration.
/// The migration contract is that every v1 backup keeps importing and comes
/// out as a v2 snapshot with empty expenses and default settings.
/// </summary>
public class SchemaV2Tests
{
    private const string V1Snapshot = """
        {
          "schemaVersion": 1,
          "nextWorkerId": 2,
          "workers": [{ "id": 1, "name": "Jan", "defaultCommissionPercentage": 50 }],
          "services": [{ "id": 1, "name": "Cut", "basePrice": 50 }],
          "products": [],
          "serviceRecords": [],
          "productSales": []
        }
        """;

    [Fact]
    public async Task V1_snapshot_in_storage_migrates_to_v2_on_load()
    {
        var storage = new InMemoryKeyValueStorage();
        storage.Items[LocalFinanceStore.StorageKey] = V1Snapshot;
        var store = new LocalFinanceStore(storage);

        var workers = await store.GetWorkersAsync();

        Assert.Equal(SnapshotLoadStatus.Ok, store.LoadStatus);
        Assert.Equal("Jan", Assert.Single(workers).Name);
        Assert.Empty(await store.GetExpensesAsync());

        var summary = await store.GetDataStateSummaryAsync();
        Assert.Equal(2, summary.SchemaVersion);
    }

    [Fact]
    public async Task V1_backup_imports_and_reexports_as_v2()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await store.ImportAsync(V1Snapshot);
        var exported = await store.ExportAsync();

        using var doc = JsonDocument.Parse(exported);
        Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("expenses").GetArrayLength());
    }

    [Fact]
    public async Task Expense_crud_roundtrip_survives_export_and_import()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        var created = await store.AddExpenseAsync(new Expense
        {
            Date = new DateTime(2026, 8, 1),
            Category = "  Rent ",
            Amount = 1200.50m,
            Note = " monthly "
        });

        Assert.Equal("Rent", created.Category);
        Assert.Equal("monthly", created.Note);

        await store.UpdateExpenseAsync(created.Id, new Expense
        {
            Id = created.Id,
            Date = new DateTime(2026, 8, 2),
            Category = "Rent",
            Amount = 1300m
        });

        var exported = await store.ExportAsync();
        var restored = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await restored.ImportAsync(exported);

        var expense = Assert.Single(await restored.GetExpensesAsync());
        Assert.Equal(1300m, expense.Amount);
        Assert.Equal(new DateTime(2026, 8, 2), expense.Date);
        Assert.Null(expense.Note);

        await restored.DeleteExpenseAsync(expense.Id);
        Assert.Empty(await restored.GetExpensesAsync());
    }

    [Fact]
    public async Task Expense_ids_keep_incrementing_after_import()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.ImportAsync("""{"schemaVersion": 2, "expenses": [{"id": 7, "date": "2026-08-01T00:00:00", "category": "Rent", "amount": 100}]}""");

        var created = await store.AddExpenseAsync(new Expense { Category = "Supplies", Amount = 20 });

        Assert.Equal(8, created.Id);
    }

    [Theory]
    [InlineData("""{"schemaVersion": 2, "expenses": [{"id": 1, "date": "2026-08-01T00:00:00", "category": "Rent", "amount": -5}]}""")]
    [InlineData("""{"schemaVersion": 2, "expenses": [{"id": 1, "date": "2026-08-01T00:00:00", "category": "", "amount": 5}]}""")]
    [InlineData("""{"schemaVersion": 2, "expenses": [{"id": 1, "category": "A", "amount": 1}, {"id": 1, "category": "B", "amount": 2}]}""")]
    public async Task Import_rejects_invalid_expenses(string json)
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(json));
    }

    [Fact]
    public async Task Currency_setting_persists_and_travels_with_backups()
    {
        var storage = new InMemoryKeyValueStorage();
        var store = new LocalFinanceStore(storage);

        await store.UpdateSettingsAsync(new AppSettings { CurrencyCode = "PLN" });

        // A fresh store over the same storage sees the persisted setting.
        var reloaded = new LocalFinanceStore(storage);
        Assert.Equal("PLN", (await reloaded.GetSettingsAsync()).CurrencyCode);

        var exported = await reloaded.ExportAsync();
        var restored = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await restored.ImportAsync(exported);
        Assert.Equal("PLN", (await restored.GetSettingsAsync()).CurrencyCode);
    }

    [Fact]
    public async Task Unknown_currency_code_normalizes_to_browser_default()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await store.UpdateSettingsAsync(new AppSettings { CurrencyCode = "DOGE" });

        Assert.Null((await store.GetSettingsAsync()).CurrencyCode);
    }

    [Fact]
    public async Task Data_state_summary_counts_expenses()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddExpenseAsync(new Expense { Category = "Rent", Amount = 100 });
        await store.AddExpenseAsync(new Expense { Category = "Supplies", Amount = 50 });

        var summary = await store.GetDataStateSummaryAsync();

        Assert.Equal(2, summary.ExpenseCount);
    }

    [Fact]
    public async Task Expense_summary_totals_by_category_and_honors_date_range()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddExpenseAsync(new Expense { Category = "Rent", Amount = 1000, Date = new DateTime(2026, 7, 1) });
        await store.AddExpenseAsync(new Expense { Category = "Supplies", Amount = 100, Date = new DateTime(2026, 8, 1) });
        await store.AddExpenseAsync(new Expense { Category = "Supplies", Amount = 60, Date = new DateTime(2026, 8, 15) });

        var all = await store.GetExpenseSummaryAsync(null, null);
        Assert.Equal(1160m, all.Total);
        Assert.Equal(2, all.ByCategory.Count);
        Assert.Equal("Rent", all.ByCategory[0].Category);

        var august = await store.GetExpenseSummaryAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        Assert.Equal(160m, august.Total);
        var supplies = Assert.Single(august.ByCategory);
        Assert.Equal(2, supplies.Count);
    }

    [Fact]
    public void Money_format_uses_selected_currency_symbol_and_position()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var money = new MoneyFormat();

            money.SetCurrency("PLN");
            Assert.Equal("1,234.50 zł", money.Format(1234.5m));

            money.SetCurrency("EUR");
            Assert.Equal("€1,234.50", money.Format(1234.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Money_format_without_setting_matches_browser_culture_default()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var money = new MoneyFormat();

            Assert.Equal(1234.5m.ToString("C", CultureInfo.CurrentCulture), money.Format(1234.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Money_format_notifies_when_currency_changes()
    {
        var money = new MoneyFormat();
        var notified = 0;
        money.OnChange += () => notified++;

        money.SetCurrency("PLN");
        money.SetCurrency("PLN");
        money.SetCurrency(null);

        Assert.Equal(2, notified);
    }
}
