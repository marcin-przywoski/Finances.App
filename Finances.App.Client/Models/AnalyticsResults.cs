namespace Finances.App.Client.Models;

public sealed record DeleteResult(bool Success, string? ErrorMessage = null);

public sealed record SummaryResult(decimal TotalRevenue, decimal TotalTips, decimal TotalWorkerShare, decimal TotalSalonShare, int RecordCount);

public sealed record DailyEarningResult(string Date, decimal Revenue, decimal Tips);

public sealed record WorkerRevenueResult(string Worker, decimal Revenue, decimal Tips);

public sealed record ServicePopularityResult(string Service, int Count, decimal Revenue);

public sealed record HistoricalPointResult(string Date, double Actual, double Trend);

public sealed record ForecastPointResult(string Date, double Predicted);

public sealed record ForecastResult(HistoricalPointResult[] Historical, ForecastPointResult[] Forecast, double Slope, double Intercept, double RSquared);

public sealed record RecentRecordResult(int Id, string Date, string Worker, string Service, decimal AmountPaid, decimal Tips, string? ClientName);

public sealed record DataStateSummary(int WorkerCount, int ServiceCount, int ProductCount, int ServiceRecordCount, int ProductSaleCount, int SchemaVersion, DateTime LastUpdatedUtc, int ApproximateSizeChars);

public sealed record MonthComparisonResult(
    decimal CurrentRevenue, decimal PreviousRevenue,
    decimal CurrentTips, decimal PreviousTips,
    int CurrentCount, int PreviousCount,
    decimal CurrentAvgTicket, decimal PreviousAvgTicket);

public sealed record DayOfWeekResult(string Day, decimal Revenue, int Count);

public sealed record TopProductResult(string Product, int Quantity, decimal Revenue);

public sealed record CombinedTimelinePoint(string Date, decimal ServiceRevenue, decimal ProductRevenue);

public sealed record CombinedTimelineResult(IReadOnlyList<CombinedTimelinePoint> Points);
