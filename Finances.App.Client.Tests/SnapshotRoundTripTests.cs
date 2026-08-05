using System.Text.Json;
using Finances.App.Client.Models;
using Finances.App.Client.Services;
using Finances.App.Client.Tests.TestDoubles;
using Finances.App.Shared;

namespace Finances.App.Client.Tests;

/// <summary>
/// Characterization tests pinning snapshot persistence: seeding, export/import
/// round-trips, id recomputation, and import validation.
/// </summary>
public class SnapshotRoundTripTests
{
    private const string StorageKey = "Finances.App.snapshot";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Fresh_store_starts_empty_and_persists_an_empty_snapshot()
    {
        var storage = new InMemoryKeyValueStorage();
        var store = new LocalFinanceStore(storage);

        var workers = await store.GetWorkersAsync();
        var services = await store.GetServicesAsync();

        Assert.Empty(workers);
        Assert.Empty(services);
        Assert.True(storage.Items.ContainsKey(StorageKey));
    }

    [Fact]
    public async Task Export_import_export_is_stable_apart_from_last_updated_timestamp()
    {
        var store = new LocalFinanceStore(TestData.CreateSeededStorage());
        await store.AddServiceRecordAsync(new ServiceRecord
        {
            WorkerId = 1,
            ServiceId = 2,
            DatePerformed = DateTime.Today,
            AmountPaid = 123.45m,
            CommissionPercentageApplied = 50,
            Tips = 7,
            ClientName = "  Client  ",
            Notes = "note"
        });

        var first = await store.ExportAsync();
        await store.ImportAsync(first);
        var second = await store.ExportAsync();

        var firstSnapshot = JsonSerializer.Deserialize<FinanceSnapshot>(first, WebJson)!;
        var secondSnapshot = JsonSerializer.Deserialize<FinanceSnapshot>(second, WebJson)!;
        secondSnapshot.LastUpdatedUtc = firstSnapshot.LastUpdatedUtc;

        Assert.Equal(
            JsonSerializer.Serialize(firstSnapshot, WebJson),
            JsonSerializer.Serialize(secondSnapshot, WebJson));
    }

    [Fact]
    public async Task Import_recomputes_next_ids_from_highest_existing_id()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var json = JsonSerializer.Serialize(new FinanceSnapshot
        {
            SchemaVersion = 1,
            NextWorkerId = 2,
            Workers =
            [
                new Worker { Id = 1, Name = "A", DefaultCommissionPercentage = 50 },
                new Worker { Id = 5, Name = "B", DefaultCommissionPercentage = 40 }
            ]
        }, WebJson);

        await store.ImportAsync(json);
        var created = await store.AddWorkerAsync(new Worker { Name = "C", DefaultCommissionPercentage = 30 });

        Assert.Equal(6, created.Id);
    }

    [Fact]
    public async Task Import_ignores_unknown_json_properties()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        const string json = """
            {
              "schemaVersion": 1,
              "someFutureField": {"nested": true},
              "workers": [{"id": 1, "name": "A", "defaultCommissionPercentage": 50}],
              "services": [],
              "products": [],
              "serviceRecords": [],
              "productSales": []
            }
            """;

        await store.ImportAsync(json);

        var workers = await store.GetWorkersAsync();
        Assert.Equal("A", Assert.Single(workers).Name);
    }

    [Fact]
    public async Task Import_rejects_invalid_json()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync("{not json"));
    }

    [Fact]
    public async Task Import_rejects_empty_content()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync("   "));
    }

    [Fact]
    public async Task Import_rejects_service_record_referencing_missing_worker()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var json = JsonSerializer.Serialize(new FinanceSnapshot
        {
            Workers = [new Worker { Id = 1, Name = "A" }],
            Services = [new Service { Id = 1, Name = "Cut", BasePrice = 10 }],
            ServiceRecords =
            [
                new ServiceRecord { Id = 1, WorkerId = 99, ServiceId = 1, DatePerformed = DateTime.Today, AmountPaid = 10 }
            ]
        }, WebJson);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(json));
    }

    [Fact]
    public async Task Import_rejects_duplicate_ids()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var json = JsonSerializer.Serialize(new FinanceSnapshot
        {
            Workers =
            [
                new Worker { Id = 1, Name = "A" },
                new Worker { Id = 1, Name = "B" }
            ]
        }, WebJson);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(json));
    }

    [Fact]
    public async Task Deleting_worker_with_service_records_is_refused()
    {
        var store = new LocalFinanceStore(TestData.CreateSeededStorage());
        await store.AddServiceRecordAsync(new ServiceRecord
        {
            WorkerId = 1,
            ServiceId = 1,
            DatePerformed = DateTime.Today,
            AmountPaid = 10,
            CommissionPercentageApplied = 50
        });

        var result = await store.DeleteWorkerAsync(1);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(3, (await store.GetWorkersAsync()).Count);
    }

    [Fact]
    public async Task Adding_product_sale_decrements_stock_when_sufficient()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });

        await store.AddProductSaleAsync(new ProductSale { ProductId = 1, DateSold = DateTime.Today, Quantity = 3, UnitPrice = 25 });

        var product = Assert.Single(await store.GetProductsAsync());
        Assert.Equal(7, product.StockQuantity);
    }
}
