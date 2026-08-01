using Finances.App.Client.Models;

namespace Finances.App.Client.Services;

/// <summary>
/// Read models and aggregations over the local data. Every method that takes
/// a workerId honors the top-bar worker filter; GetRevenueByWorkerAsync is
/// inherently a cross-worker comparison and takes none.
/// </summary>
public interface IAnalyticsService
{
    Task<SummaryResult> GetSummaryAsync(int? workerId, DateTime? fromDate, DateTime? toDate);
    Task<IReadOnlyList<DailyEarningResult>> GetDailyEarningsAsync(int? workerId, DateTime? fromDate, DateTime? toDate);
    Task<IReadOnlyList<WorkerRevenueResult>> GetRevenueByWorkerAsync(DateTime? fromDate, DateTime? toDate);
    Task<IReadOnlyList<ServicePopularityResult>> GetServicePopularityAsync(int? workerId, DateTime? fromDate, DateTime? toDate);
    Task<ForecastResult> GetForecastAsync(int? workerId, int forecastDays, DateTime? fromDate = null, DateTime? toDate = null);
    Task<IReadOnlyList<RecentRecordResult>> GetRecentAsync(int count);
    Task<MonthComparisonResult> GetMonthComparisonAsync(int? workerId);
    Task<IReadOnlyList<DayOfWeekResult>> GetRevenueByDayOfWeekAsync(int? workerId, DateTime? fromDate, DateTime? toDate);
    Task<IReadOnlyList<TopProductResult>> GetTopProductsAsync(int? workerId, DateTime? fromDate, DateTime? toDate, int count = 5);
    Task<CombinedTimelineResult> GetCombinedTimelineAsync(int? workerId, DateTime? fromDate, DateTime? toDate);
}
