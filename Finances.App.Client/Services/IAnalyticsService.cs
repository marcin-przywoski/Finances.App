using Finances.App.Client.Models;

namespace Finances.App.Client.Services;

/// <summary>
/// Read models and aggregations over the local data. Every method that takes
/// a workerId honors the top-bar worker filter; GetRevenueByWorkerAsync is
/// inherently a cross-worker comparison and takes none.
/// </summary>
public interface IAnalyticsService
{
    Task<SummaryResult> GetSummaryAsync(int? workerId, DateTime? from, DateTime? to);
    Task<IReadOnlyList<DailyEarningResult>> GetDailyEarningsAsync(int? workerId, DateTime? from, DateTime? to);
    Task<IReadOnlyList<WorkerRevenueResult>> GetRevenueByWorkerAsync(DateTime? from, DateTime? to);
    Task<IReadOnlyList<ServicePopularityResult>> GetServicePopularityAsync(int? workerId, DateTime? from, DateTime? to);
    Task<ForecastResult> GetForecastAsync(int? workerId, int forecastDays, DateTime? from = null, DateTime? to = null);
    Task<IReadOnlyList<RecentRecordResult>> GetRecentAsync(int count);
    Task<MonthComparisonResult> GetMonthComparisonAsync(int? workerId);
    Task<IReadOnlyList<DayOfWeekResult>> GetRevenueByDayOfWeekAsync(int? workerId, DateTime? from, DateTime? to);
    Task<IReadOnlyList<TopProductResult>> GetTopProductsAsync(int? workerId, DateTime? from, DateTime? to, int count = 5);
    Task<CombinedTimelineResult> GetCombinedTimelineAsync(int? workerId, DateTime? from, DateTime? to);
}
