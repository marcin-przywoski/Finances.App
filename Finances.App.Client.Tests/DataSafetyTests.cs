using System.Text.Json;
using Finances.App.Client.Models;
using Finances.App.Client.Services;
using Finances.App.Client.Services.Storage;
using Finances.App.Client.Tests.TestDoubles;
using Finances.App.Shared;

namespace Finances.App.Client.Tests;

/// <summary>
/// Tests for the Phase 2 persistence-lifecycle hardening: quarantine instead
/// of overwrite, validation on load, save error handling, multi-tab guards,
/// stock integrity, and serialization format.
/// </summary>
public class DataSafetyTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static string SnapshotJson(FinanceSnapshot snapshot) => JsonSerializer.Serialize(snapshot, WebJson);

    [Fact]
    public async Task Corrupt_stored_json_is_quarantined_not_overwritten()
    {
        var storage = new InMemoryKeyValueStorage();
        storage.Items[LocalFinanceStore.StorageKey] = "{definitely not json";
        var store = new LocalFinanceStore(storage);

        var workers = await store.GetWorkersAsync();

        Assert.Empty(workers);
        Assert.Equal(SnapshotLoadStatus.RecoveredFromCorruptData, store.LoadStatus);
        Assert.Equal("{definitely not json", storage.Items[LocalFinanceStore.QuarantineKey]);
    }

    [Fact]
    public async Task Stored_data_with_dangling_references_is_quarantined_on_load()
    {
        var storage = new InMemoryKeyValueStorage();
        var corrupt = SnapshotJson(new FinanceSnapshot
        {
            Workers = [new Worker { Id = 1, Name = "A" }],
            Services = [new Service { Id = 1, Name = "Cut", BasePrice = 10 }],
            ServiceRecords =
            [
                new ServiceRecord { Id = 1, WorkerId = 42, ServiceId = 1, DatePerformed = DateTime.Today, AmountPaid = 10 }
            ]
        });
        storage.Items[LocalFinanceStore.StorageKey] = corrupt;
        var store = new LocalFinanceStore(storage);

        await store.GetWorkersAsync();

        Assert.Equal(SnapshotLoadStatus.RecoveredFromCorruptData, store.LoadStatus);
        Assert.Equal(corrupt, storage.Items[LocalFinanceStore.QuarantineKey]);
    }

    [Fact]
    public async Task Snapshot_from_newer_app_version_is_quarantined_on_load()
    {
        var storage = new InMemoryKeyValueStorage();
        var futureData = """{"schemaVersion": 99, "workers": [], "services": [], "products": [], "serviceRecords": [], "productSales": []}""";
        storage.Items[LocalFinanceStore.StorageKey] = futureData;
        var store = new LocalFinanceStore(storage);

        await store.GetWorkersAsync();

        Assert.Equal(SnapshotLoadStatus.RecoveredFromCorruptData, store.LoadStatus);
        Assert.Equal(futureData, storage.Items[LocalFinanceStore.QuarantineKey]);
    }

    [Fact]
    public async Task Import_rejects_backup_from_newer_app_version_with_clear_message()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ImportAsync("""{"schemaVersion": 3, "workers": []}"""));

        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public async Task Clearing_quarantine_removes_preserved_copy_and_resets_status()
    {
        var storage = new InMemoryKeyValueStorage();
        storage.Items[LocalFinanceStore.StorageKey] = "{bad";
        var store = new LocalFinanceStore(storage);
        await store.GetWorkersAsync();

        await store.ClearQuarantineAsync();

        Assert.Equal(SnapshotLoadStatus.Ok, store.LoadStatus);
        Assert.False(storage.Items.ContainsKey(LocalFinanceStore.QuarantineKey));
    }

    [Fact]
    public async Task Import_with_null_worker_name_fails_with_friendly_message_not_a_crash()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        const string json = """{"schemaVersion": 1, "workers": [{"id": 1, "name": null, "defaultCommissionPercentage": 50}]}""";

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(json));

        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"workers": [{"id": 1, "name": "A", "defaultCommissionPercentage": 500}]}""")]
    [InlineData("""{"services": [{"id": 1, "name": "Cut", "basePrice": -5}]}""")]
    [InlineData("""{"products": [{"id": 1, "name": "P", "price": -1, "stockQuantity": 0}]}""")]
    [InlineData("""{"products": [{"id": 1, "name": "P", "price": 1, "stockQuantity": -3}]}""")]
    public async Task Import_rejects_insane_money_values(string json)
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(json));
    }

    [Fact]
    public async Task Import_rejects_negative_amount_on_service_record()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var json = SnapshotJson(new FinanceSnapshot
        {
            Workers = [new Worker { Id = 1, Name = "A" }],
            Services = [new Service { Id = 1, Name = "Cut", BasePrice = 10 }],
            ServiceRecords =
            [
                new ServiceRecord { Id = 1, WorkerId = 1, ServiceId = 1, DatePerformed = DateTime.Today, AmountPaid = -999999 }
            ]
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(json));
    }

    [Fact]
    public async Task Failed_save_reverts_to_last_persisted_state_and_reports_storage_error()
    {
        var storage = new ThrowingKeyValueStorage();
        var store = new LocalFinanceStore(storage);
        await store.GetWorkersAsync();

        storage.FailWrites = true;
        await Assert.ThrowsAsync<StorageWriteException>(() =>
            store.AddWorkerAsync(new Worker { Name = "New", DefaultCommissionPercentage = 10 }));

        storage.FailWrites = false;
        var workers = await store.GetWorkersAsync();
        Assert.DoesNotContain(workers, worker => worker.Name == "New");
    }

    [Fact]
    public async Task Save_refuses_to_clobber_data_written_by_another_tab()
    {
        var storage = new InMemoryKeyValueStorage();
        var store = new LocalFinanceStore(storage);
        await store.GetWorkersAsync();

        // Another tab saved: it would have replaced the snapshot and revision.
        storage.Items[LocalFinanceStore.RevisionKey] = "other-tab-revision";

        await Assert.ThrowsAsync<ConcurrentUpdateException>(() =>
            store.AddWorkerAsync(new Worker { Name = "Lost", DefaultCommissionPercentage = 10 }));

        // The store refreshed from storage; the rejected change is gone.
        var workers = await store.GetWorkersAsync();
        Assert.DoesNotContain(workers, worker => worker.Name == "Lost");
    }

    [Fact]
    public async Task External_change_notification_reloads_data_from_storage()
    {
        var storage = new InMemoryKeyValueStorage();
        var store = new LocalFinanceStore(storage);
        await store.GetWorkersAsync();

        var replacement = SnapshotJson(new FinanceSnapshot
        {
            Workers = [new Worker { Id = 7, Name = "FromOtherTab", DefaultCommissionPercentage = 10 }]
        });
        storage.Items[LocalFinanceStore.StorageKey] = replacement;
        store.HandleExternalDataChange();

        var workers = await store.GetWorkersAsync();

        Assert.Equal("FromOtherTab", Assert.Single(workers).Name);
    }

    [Fact]
    public async Task Concurrent_first_loads_share_one_storage_read()
    {
        var storage = new InMemoryKeyValueStorage { SimulateAsync = true };
        var store = new LocalFinanceStore(storage);

        var first = store.GetWorkersAsync();
        var second = store.GetServicesAsync();
        await Task.WhenAll(first, second);

        // One load = one snapshot read + one revision read.
        Assert.Equal(2, storage.ReadCount);
    }

    [Fact]
    public async Task Overselling_a_product_is_rejected_and_nothing_is_recorded()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 3 });

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() =>
            store.AddProductSaleAsync(new ProductSale { ProductId = 1, DateSold = DateTime.Today, Quantity = 10, UnitPrice = 25 }));

        Assert.Equal(3, ex.Available);
        Assert.Empty(await store.GetProductSalesAsync());
        Assert.Equal(3, (await store.GetProductsAsync()).Single().StockQuantity);
    }

    [Fact]
    public async Task Updating_a_sale_quantity_adjusts_stock()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });
        var sale = await store.AddProductSaleAsync(new ProductSale { ProductId = 1, DateSold = DateTime.Today, Quantity = 2, UnitPrice = 25 });

        var update = new ProductSale { Id = sale.Id, ProductId = 1, DateSold = DateTime.Today, Quantity = 5, UnitPrice = 25 };
        await store.UpdateProductSaleAsync(sale.Id, update);

        Assert.Equal(5, (await store.GetProductsAsync()).Single().StockQuantity);
    }

    [Fact]
    public async Task Updating_a_sale_to_another_product_moves_the_stock_effect()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });
        await store.AddProductAsync(new Product { Name = "Shampoo", Price = 10, StockQuantity = 10 });
        var sale = await store.AddProductSaleAsync(new ProductSale { ProductId = 1, DateSold = DateTime.Today, Quantity = 4, UnitPrice = 25 });

        var update = new ProductSale { Id = sale.Id, ProductId = 2, DateSold = DateTime.Today, Quantity = 3, UnitPrice = 10 };
        await store.UpdateProductSaleAsync(sale.Id, update);

        var products = await store.GetProductsAsync();
        Assert.Equal(10, products.Single(product => product.Name == "Pomade").StockQuantity);
        Assert.Equal(7, products.Single(product => product.Name == "Shampoo").StockQuantity);
    }

    [Fact]
    public async Task Updating_a_sale_beyond_available_stock_is_rejected_without_side_effects()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 5 });
        var sale = await store.AddProductSaleAsync(new ProductSale { ProductId = 1, DateSold = DateTime.Today, Quantity = 2, UnitPrice = 25 });

        var update = new ProductSale { Id = sale.Id, ProductId = 1, DateSold = DateTime.Today, Quantity = 8, UnitPrice = 25 };
        await Assert.ThrowsAsync<InsufficientStockException>(() => store.UpdateProductSaleAsync(sale.Id, update));

        Assert.Equal(3, (await store.GetProductsAsync()).Single().StockQuantity);
        Assert.Equal(2, (await store.GetProductSalesAsync()).Single().Quantity);
    }

    [Fact]
    public async Task Deleting_a_sale_restores_stock()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });
        var sale = await store.AddProductSaleAsync(new ProductSale { ProductId = 1, DateSold = DateTime.Today, Quantity = 4, UnitPrice = 25 });

        await store.DeleteProductSaleAsync(sale.Id);

        Assert.Equal(10, (await store.GetProductsAsync()).Single().StockQuantity);
    }

    [Fact]
    public async Task Reset_preserves_outgoing_data_under_side_key()
    {
        var storage = new InMemoryKeyValueStorage();
        var store = new LocalFinanceStore(storage);
        await store.AddWorkerAsync(new Worker { Name = "Keep me", DefaultCommissionPercentage = 10 });

        await store.ResetAsync();

        Assert.True(storage.Items.ContainsKey(LocalFinanceStore.PreResetKey));
        Assert.Contains("Keep me", storage.Items[LocalFinanceStore.PreResetKey]);
        Assert.Empty(await store.GetWorkersAsync());
    }

    [Fact]
    public async Task Persisted_snapshot_is_compact_while_export_stays_readable()
    {
        var storage = new InMemoryKeyValueStorage();
        var store = new LocalFinanceStore(storage);
        await store.GetWorkersAsync();

        var persisted = storage.Items[LocalFinanceStore.StorageKey];
        var exported = await store.ExportAsync();

        Assert.DoesNotContain('\n', persisted);
        Assert.Contains("\"schemaVersion\"", persisted);
        Assert.Contains('\n', exported);
        Assert.Contains("\"schemaVersion\"", exported);
    }

    [Fact]
    public async Task Legacy_backup_format_with_computed_properties_still_imports()
    {
        // Produced by earlier builds: indented Web-defaults JSON including
        // read-only computed properties and null navigation properties.
        const string legacy = """
            {
              "schemaVersion": 1,
              "lastUpdatedUtc": "2026-04-01T10:00:00Z",
              "nextWorkerId": 2,
              "nextServiceId": 2,
              "nextProductId": 1,
              "nextServiceRecordId": 2,
              "nextProductSaleId": 1,
              "workers": [
                { "id": 1, "name": "Jan", "defaultCommissionPercentage": 50, "applicationUserId": null }
              ],
              "services": [
                { "id": 1, "name": "Cut", "basePrice": 50 }
              ],
              "products": [],
              "serviceRecords": [
                {
                  "id": 1,
                  "workerId": 1,
                  "worker": null,
                  "serviceId": 1,
                  "service": null,
                  "datePerformed": "2026-04-01T00:00:00",
                  "amountPaid": 50,
                  "commissionPercentageApplied": 50,
                  "tips": 5,
                  "clientName": null,
                  "notes": null,
                  "workerShare": 25,
                  "salonShare": 25
                }
              ],
              "productSales": []
            }
            """;
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await store.ImportAsync(legacy);

        var records = await store.GetServiceRecordsAsync();
        var record = Assert.Single(records);
        Assert.Equal(50, record.AmountPaid);
        Assert.Equal(25, record.WorkerShare);
        Assert.Equal("Jan", Assert.Single(await store.GetWorkersAsync()).Name);
    }
}
