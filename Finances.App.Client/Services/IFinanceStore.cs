using Finances.App.Client.Models;
using Finances.App.Shared;

namespace Finances.App.Client.Services;

/// <summary>
/// CRUD and snapshot-lifecycle surface of the local data store.
/// </summary>
public interface IFinanceStore
{
    event Action? OnChange;

    SnapshotLoadStatus LoadStatus { get; }
    Task<SnapshotLoadStatus> GetLoadStatusAsync();
    void HandleExternalDataChange();
    ValueTask<string?> GetQuarantinedJsonAsync();
    Task ClearQuarantineAsync();

    Task<IReadOnlyList<Worker>> GetWorkersAsync();
    Task<Worker> AddWorkerAsync(Worker worker);
    Task UpdateWorkerAsync(int id, Worker worker);
    Task<DeleteResult> DeleteWorkerAsync(int id);

    Task<IReadOnlyList<Service>> GetServicesAsync();
    Task<Service> AddServiceAsync(Service service);
    Task UpdateServiceAsync(int id, Service service);
    Task<DeleteResult> DeleteServiceAsync(int id);

    Task<IReadOnlyList<Product>> GetProductsAsync();
    Task<Product> AddProductAsync(Product product);
    Task UpdateProductAsync(int id, Product product);
    Task<DeleteResult> DeleteProductAsync(int id);

    Task<IReadOnlyList<ServiceRecord>> GetServiceRecordsAsync();
    Task<ServiceRecord> AddServiceRecordAsync(ServiceRecord record);
    Task UpdateServiceRecordAsync(int id, ServiceRecord record);
    Task DeleteServiceRecordAsync(int id);

    Task<IReadOnlyList<ProductSale>> GetProductSalesAsync();
    Task<ProductSale> AddProductSaleAsync(ProductSale sale);
    Task UpdateProductSaleAsync(int id, ProductSale sale);
    Task DeleteProductSaleAsync(int id);

    Task<IReadOnlyList<Expense>> GetExpensesAsync();
    Task<Expense> AddExpenseAsync(Expense expense);
    Task UpdateExpenseAsync(int id, Expense expense);
    Task DeleteExpenseAsync(int id);

    Task<AppSettings> GetSettingsAsync();
    Task UpdateSettingsAsync(AppSettings settings);

    Task<string> ExportAsync();
    Task ImportAsync(string json);
    Task ResetAsync();
    ValueTask<string?> GetPreResetJsonAsync();
    Task<DataStateSummary> GetDataStateSummaryAsync();
}
