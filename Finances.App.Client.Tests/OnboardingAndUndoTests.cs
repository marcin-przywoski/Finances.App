using Finances.App.Client.Services;
using Finances.App.Client.Tests.TestDoubles;
using Finances.App.Shared;

namespace Finances.App.Client.Tests;

/// <summary>
/// Phase 3 behaviors: the empty first-run snapshot, the product
/// referential-integrity guard, the pre-import backup, and the store
/// semantics the page-level undo relies on.
/// </summary>
public class OnboardingAndUndoTests
{
    [Fact]
    public async Task Corrupt_data_recovery_starts_empty_not_with_placeholder_people()
    {
        var storage = new InMemoryKeyValueStorage();
        storage.Items[LocalFinanceStore.StorageKey] = "{broken";
        var store = new LocalFinanceStore(storage);

        Assert.Empty(await store.GetWorkersAsync());
        Assert.Empty(await store.GetServicesAsync());
    }

    [Fact]
    public async Task Deleting_product_with_sales_is_refused()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var product = await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });
        await store.AddProductSaleAsync(new ProductSale { ProductId = product.Id, DateSold = DateTime.Today, Quantity = 1, UnitPrice = 25 });

        var result = await store.DeleteProductAsync(product.Id);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Single(await store.GetProductsAsync());
    }

    [Fact]
    public async Task Deleting_product_without_sales_succeeds()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var product = await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });

        var result = await store.DeleteProductAsync(product.Id);

        Assert.True(result.Success);
        Assert.Empty(await store.GetProductsAsync());
    }

    [Fact]
    public async Task Import_preserves_outgoing_data_and_restore_brings_it_back()
    {
        var storage = new InMemoryKeyValueStorage();
        var store = new LocalFinanceStore(storage);
        await store.AddWorkerAsync(new Worker { Name = "Original", DefaultCommissionPercentage = 40 });

        await store.ImportAsync("""{"schemaVersion": 2, "workers": [{"id": 1, "name": "Imported", "defaultCommissionPercentage": 50}]}""");
        Assert.Equal("Imported", Assert.Single(await store.GetWorkersAsync()).Name);

        // The pre-import copy is available and round-trips through import.
        var backup = await store.GetPreResetJsonAsync();
        Assert.NotNull(backup);
        Assert.Contains("Original", backup);

        await store.ImportAsync(backup!);
        Assert.Equal("Original", Assert.Single(await store.GetWorkersAsync()).Name);
    }

    [Fact]
    public async Task Deleted_sale_can_be_readded_and_stock_is_redecremented()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var product = await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });
        var sale = await store.AddProductSaleAsync(new ProductSale { ProductId = product.Id, DateSold = DateTime.Today, Quantity = 4, UnitPrice = 25 });

        await store.DeleteProductSaleAsync(sale.Id);
        Assert.Equal(10, (await store.GetProductsAsync()).Single().StockQuantity);

        // What the page-level Undo does: re-add the deleted sale.
        await store.AddProductSaleAsync(sale);

        Assert.Equal(6, (await store.GetProductsAsync()).Single().StockQuantity);
        Assert.Equal(4, (await store.GetProductSalesAsync()).Single().Quantity);
    }

    [Fact]
    public async Task Undo_of_sale_fails_cleanly_when_stock_was_consumed_meanwhile()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var product = await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 4 });
        var sale = await store.AddProductSaleAsync(new ProductSale { ProductId = product.Id, DateSold = DateTime.Today, Quantity = 4, UnitPrice = 25 });

        await store.DeleteProductSaleAsync(sale.Id);

        // Another sale eats the returned stock before the undo happens.
        await store.AddProductSaleAsync(new ProductSale { ProductId = product.Id, DateSold = DateTime.Today, Quantity = 3, UnitPrice = 25 });

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddProductSaleAsync(sale));
        Assert.Equal(1, (await store.GetProductsAsync()).Single().StockQuantity);
    }
}
