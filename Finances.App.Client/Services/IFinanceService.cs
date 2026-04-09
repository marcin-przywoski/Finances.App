using Finances.App.Shared;

namespace Finances.App.Client.Services;

public interface IFinanceService
{
    event Action? OnChange;

    // Workers
    Task<IReadOnlyList<Worker>> GetWorkersAsync();
    Task<Worker> AddWorkerAsync(Worker worker);
    Task UpdateWorkerAsync(int id, Worker worker);
    Task<DeleteResult> DeleteWorkerAsync(int id);

    // Services
    Task<IReadOnlyList<Service>> GetServicesAsync();
    Task<Service> AddServiceAsync(Service service);
    Task UpdateServiceAsync(int id, Service service);
    Task<DeleteResult> DeleteServiceAsync(int id);

    // Products
    Task<IReadOnlyList<Product>> GetProductsAsync();
    Task<Product> AddProductAsync(Product product);
    Task UpdateProductAsync(int id, Product product);
    Task<DeleteResult> DeleteProductAsync(int id);

    // Service Records
    Task<IReadOnlyList<ServiceRecord>> GetServiceRecordsAsync();
    Task<ServiceRecord> AddServiceRecordAsync(ServiceRecord record);
    Task UpdateServiceRecordAsync(int id, ServiceRecord record);
    Task DeleteServiceRecordAsync(int id);

    // Product Sales
    Task<IReadOnlyList<ProductSale>> GetProductSalesAsync();
    Task<ProductSale> AddProductSaleAsync(ProductSale sale);
    Task UpdateProductSaleAsync(int id, ProductSale sale);
    Task DeleteProductSaleAsync(int id);

    // Clients
    Task<IReadOnlyList<SalonClient>> GetClientsAsync();
    Task<SalonClient> AddClientAsync(SalonClient client);
    Task UpdateClientAsync(int id, SalonClient client);
    Task<DeleteResult> DeleteClientAsync(int id);
    Task<SalonClient?> GetOrCreateClientByNameAsync(string? name);
    Task<ClientDetailResult> GetClientDetailAsync(int clientId);
    Task<IReadOnlyList<TopClientResult>> GetTopClientsAsync(int? workerId, DateTime? from, DateTime? to, int count = 10);

    // Recurring Services
    Task<IReadOnlyList<RecurringService>> GetRecurringServicesAsync();
    Task<RecurringService> AddRecurringServiceAsync(RecurringService recurring);
    Task UpdateRecurringServiceAsync(int id, RecurringService recurring);
    Task DeleteRecurringServiceAsync(int id);
    Task<IReadOnlyList<RecurringService>> GetDueRecurringServicesAsync();

    // Goals
    Task<IReadOnlyList<Goal>> GetGoalsAsync();
    Task<Goal> AddGoalAsync(Goal goal);
    Task UpdateGoalAsync(int id, Goal goal);
    Task DeleteGoalAsync(int id);
    Task<IReadOnlyList<GoalProgressResult>> GetGoalProgressAsync(int? workerId);

    // Analytics
    Task<SummaryResult> GetSummaryAsync(int? workerId, DateTime? from, DateTime? to);
    Task<IReadOnlyList<DailyEarningResult>> GetDailyEarningsAsync(int? workerId, DateTime? from, DateTime? to);
    Task<IReadOnlyList<WorkerRevenueResult>> GetRevenueByWorkerAsync(int? workerId, DateTime? from, DateTime? to);
    Task<IReadOnlyList<ServicePopularityResult>> GetServicePopularityAsync(int? workerId, DateTime? from, DateTime? to);
    Task<ForecastResult> GetForecastAsync(int? workerId, DateTime? from, DateTime? to, int forecastDays);
    Task<IReadOnlyList<RecentRecordResult>> GetRecentAsync(int? workerId, int count);
    Task<decimal> GetProductSalesRevenueAsync(DateTime? from, DateTime? to);
    Task<MonthComparisonResult> GetMonthComparisonAsync(int? workerId);
    Task<IReadOnlyList<DayOfWeekResult>> GetRevenueByDayOfWeekAsync(int? workerId, DateTime? from, DateTime? to);
    Task<IReadOnlyList<TopProductResult>> GetTopProductsAsync(int? workerId, DateTime? from, DateTime? to, int count = 5);
    Task<CombinedTimelineResult> GetCombinedTimelineAsync(int? workerId, DateTime? from, DateTime? to);
    Task<DataStateSummary> GetDataStateSummaryAsync();

    // Data management
    Task<string> ExportAsync();
    Task ImportAsync(string json);
    Task ResetAsync();
}
