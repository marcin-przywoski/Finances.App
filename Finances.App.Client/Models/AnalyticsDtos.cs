namespace Finances.App.Client.Models;

public sealed record SummaryDto(
    decimal TotalRevenue,
    decimal TotalTips,
    decimal TotalWorkerShare,
    decimal TotalSalonShare,
    int RecordCount);

public sealed record RecentRecordDto(
    int Id,
    string Date,
    string Worker,
    string Service,
    decimal AmountPaid,
    decimal Tips,
    string? ClientName);

public sealed record MonthComparisonDto(
    decimal CurrentRevenue,
    decimal PreviousRevenue,
    decimal CurrentTips,
    decimal PreviousTips,
    int CurrentCount,
    int PreviousCount,
    decimal CurrentAvgTicket,
    decimal PreviousAvgTicket);

public sealed record WorkerRevenueDto(string Worker, decimal Revenue, decimal Tips);

public sealed record ServicePopularityDto(string Service, int Count, decimal Revenue);

public sealed record DailyEarningDto(string Date, decimal Revenue, decimal Tips);

public sealed record HistoricalPointDto(string Date, double Actual, double Trend);

public sealed record ForecastPointDto(string Date, double Predicted);

public sealed record ForecastResultDto(
    HistoricalPointDto[] Historical,
    ForecastPointDto[] Forecast,
    double Slope,
    double Intercept,
    double RSquared);

public sealed record DayOfWeekDto(string Day, decimal Revenue, int Count);

public sealed record TopProductDto(string Product, int Quantity, decimal Revenue);

public sealed record CombinedTimelinePointDto(string Date, decimal ServiceRevenue, decimal ProductRevenue);

public sealed record CombinedTimelineDto(CombinedTimelinePointDto[] Points);

public sealed record ApiErrorDto(string? Message);
