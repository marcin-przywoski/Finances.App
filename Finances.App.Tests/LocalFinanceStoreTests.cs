using System.Text.Json;
using Finances.App.Client.Services;
using Finances.App.Shared;
using Xunit;

namespace Finances.App.Tests;

public class LocalFinanceStoreTests
{
    private const string SnapshotKey = "Finances.App.snapshot";
    private const string BackupKeyPrefix = "Finances.App.backup.v";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ExpenseCrud_RoundTrips_AcrossStoreInstances()
    {
        var js = new FakeJSRuntime();
        var store = new LocalFinanceStore(js);

        var created = await store.AddExpenseAsync(new Expense
        {
            Date = new DateTime(2025, 6, 15),
            Category = ExpenseCategory.Rent,
            Amount = 1200m,
            Vendor = "Landlord",
            Notes = "Monthly salon rent"
        });

        Assert.True(created.Id > 0);

        var all = await store.GetExpensesAsync();
        Assert.Single(all);
        Assert.Equal(1200m, all[0].Amount);
        Assert.Equal(ExpenseCategory.Rent, all[0].Category);

        created.Amount = 1400m;
        created.Notes = "Updated";
        await store.UpdateExpenseAsync(created.Id, created);

        var updated = (await store.GetExpensesAsync()).Single();
        Assert.Equal(1400m, updated.Amount);
        Assert.Equal("Updated", updated.Notes);

        // A new store on the same fake storage should read the persisted state.
        var reopened = new LocalFinanceStore(js);
        var persisted = await reopened.GetExpensesAsync();
        Assert.Single(persisted);
        Assert.Equal(1400m, persisted[0].Amount);

        await reopened.DeleteExpenseAsync(created.Id);
        Assert.Empty(await reopened.GetExpensesAsync());
    }

    [Fact]
    public async Task ClientCommentReminder_Surfaces_WhenDue_AndDisappears_WhenHandled()
    {
        var js = new FakeJSRuntime();
        var store = new LocalFinanceStore(js);

        var client = await store.AddClientAsync(new SalonClient { Name = "Alice" });

        var reminderInThePast = DateTime.UtcNow.AddMinutes(-5);
        var comment = await store.AddClientCommentAsync(new ClientComment
        {
            ClientId = client.Id,
            Body = "Call about balayage follow-up",
            ReminderUtc = reminderInThePast
        });

        var due = await store.GetDueClientCommentRemindersAsync();
        Assert.Single(due);
        Assert.Equal(comment.Id, due[0].Id);
        Assert.NotNull(due[0].Client);
        Assert.Equal("Alice", due[0].Client!.Name);

        await store.MarkCommentReminderHandledAsync(comment.Id);

        Assert.Empty(await store.GetDueClientCommentRemindersAsync());

        var stored = (await store.GetClientCommentsAsync(client.Id)).Single();
        Assert.True(stored.ReminderHandled);
    }

    [Fact]
    public async Task Migration_FromV4_AddsV5Collections_AndWritesBackup()
    {
        var js = new FakeJSRuntime();

        var legacy = new LegacyV4Snapshot
        {
            SchemaVersion = 4,
            LastUpdatedUtc = DateTime.UtcNow,
            NextWorkerId = 2,
            NextClientId = 2,
            Workers = { new Worker { Id = 1, Name = "Kasia", DefaultCommissionPercentage = 50 } },
            Clients = { new SalonClient { Id = 1, Name = "Alice", CreatedDate = DateTime.Today } },
            Services = { new Service { Id = 1, Name = "Haircut", BasePrice = 80 } },
            Products = { },
            ServiceRecords = { },
            ProductSales = { },
            RecurringServices = { },
            Goals = { }
        };

        var serialized = JsonSerializer.Serialize(legacy, SerializerOptions);
        js.SetItem(SnapshotKey, serialized);

        var store = new LocalFinanceStore(js);

        // Trigger migration by reading any public list.
        var expenses = await store.GetExpensesAsync();
        Assert.Empty(expenses);

        var invoices = await store.GetInvoicesAsync();
        Assert.Empty(invoices);

        var comments = await store.GetClientCommentsAsync(1);
        Assert.Empty(comments);

        // Backup of the v4 snapshot should be preserved so users can recover.
        Assert.True(js.LocalStorage.ContainsKey($"{BackupKeyPrefix}4"),
            "Expected a v4 backup key after migrating to v5.");

        // Snapshot should now be at the current schema version on disk.
        var summary = await store.GetDataStateSummaryAsync();
        Assert.Equal(5, summary.SchemaVersion);

        // The migrated snapshot should preserve existing entities.
        var workers = await store.GetWorkersAsync();
        Assert.Single(workers);
        Assert.Equal("Kasia", workers[0].Name);

        var clients = await store.GetClientsAsync();
        Assert.Single(clients);
        Assert.Equal("Alice", clients[0].Name);
    }

    [Fact]
    public async Task NetProfit_Subtracts_Expenses_From_Revenue()
    {
        var js = new FakeJSRuntime();
        var store = new LocalFinanceStore(js);

        var worker = await store.AddWorkerAsync(new Worker { Name = "Ola", DefaultCommissionPercentage = 50 });
        var service = await store.AddServiceAsync(new Service { Name = "Haircut", BasePrice = 100 });

        await store.AddServiceRecordAsync(new ServiceRecord
        {
            WorkerId = worker.Id,
            ServiceId = service.Id,
            AmountPaid = 200m,
            Tips = 20m,
            DatePerformed = DateTime.Today
        });

        await store.AddExpenseAsync(new Expense
        {
            Date = DateTime.Today,
            Category = ExpenseCategory.Supplies,
            Amount = 50m,
            Vendor = "Supplier"
        });

        var result = await store.GetNetProfitAsync(null, null, null);

        // Revenue intentionally excludes tips: only the paid service amount counts
        // toward the business bottom line. Tips flow through to workers separately.
        Assert.Equal(200m, result.Revenue);
        Assert.Equal(50m, result.Expenses);
        Assert.Equal(150m, result.Net);
    }

    /// <summary>
    /// Shape mirroring <c>FinanceSnapshot</c> at schema v4 — before ClientComments,
    /// Expenses, Invoices, InvoiceLineItems, and Attachments were introduced.
    /// Used to reconstruct legacy data for migration tests.
    /// </summary>
    private sealed class LegacyV4Snapshot
    {
        public int SchemaVersion { get; set; } = 4;
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public int NextWorkerId { get; set; }
        public int NextServiceId { get; set; }
        public int NextProductId { get; set; }
        public int NextServiceRecordId { get; set; }
        public int NextProductSaleId { get; set; }
        public int NextClientId { get; set; }
        public int NextRecurringServiceId { get; set; }
        public int NextGoalId { get; set; }
        public List<Worker> Workers { get; set; } = new();
        public List<Service> Services { get; set; } = new();
        public List<Product> Products { get; set; } = new();
        public List<ServiceRecord> ServiceRecords { get; set; } = new();
        public List<ProductSale> ProductSales { get; set; } = new();
        public List<SalonClient> Clients { get; set; } = new();
        public List<RecurringService> RecurringServices { get; set; } = new();
        public List<Goal> Goals { get; set; } = new();
    }
}
