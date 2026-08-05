using Finances.App.Client.Models;
using Finances.App.Shared;

namespace Finances.App.Client.Services;

/// <summary>
/// Read-model / analytics side of the store: aggregations over the snapshot,
/// including the least-squares revenue forecast.
/// </summary>
public sealed partial class LocalFinanceStore
{
    public async Task<SummaryResult> GetSummaryAsync(int? workerId, DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLoadedAsync();

        var records = FilterStoredRecords(workerId, fromDate, toDate).ToList();
        var totalRevenue = records.Sum(record => record.AmountPaid);
        var totalTips = records.Sum(record => record.Tips);
        // Sum the rounded per-record shares so totals match the rows users see.
        var totalWorkerShare = records.Sum(record => record.WorkerShare);
        var totalSalonShare = totalRevenue - totalWorkerShare;

        return new SummaryResult(totalRevenue, totalTips, totalWorkerShare, totalSalonShare, records.Count);
    }

    public async Task<IReadOnlyList<DailyEarningResult>> GetDailyEarningsAsync(int? workerId, DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLoadedAsync();

        return FilterStoredRecords(workerId, fromDate, toDate)
            .GroupBy(record => record.DatePerformed.Date)
            .Select(group => new DailyEarningResult(
                group.Key.ToDateKey(),
                group.Sum(record => record.AmountPaid),
                group.Sum(record => record.Tips)))
            .OrderBy(item => item.Date, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<WorkerRevenueResult>> GetRevenueByWorkerAsync(DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLoadedAsync();

        var workerNames = _snapshot!.Workers.ToDictionary(worker => worker.Id, worker => worker.Name);

        return FilterStoredRecords(null, fromDate, toDate)
            .GroupBy(record => workerNames.GetValueOrDefault(record.WorkerId, "Unknown"))
            .Select(group => new WorkerRevenueResult(
                group.Key,
                group.Sum(record => record.AmountPaid),
                group.Sum(record => record.Tips)))
            .OrderByDescending(item => item.Revenue)
            .ToList();
    }

    public async Task<IReadOnlyList<ServicePopularityResult>> GetServicePopularityAsync(int? workerId, DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLoadedAsync();

        var serviceNames = _snapshot!.Services.ToDictionary(service => service.Id, service => service.Name);

        return FilterStoredRecords(workerId, fromDate, toDate)
            .GroupBy(record => serviceNames.GetValueOrDefault(record.ServiceId, "Unknown"))
            .Select(group => new ServicePopularityResult(
                group.Key,
                group.Count(),
                group.Sum(record => record.AmountPaid)))
            .OrderByDescending(item => item.Count)
            .ToList();
    }

    public async Task<ForecastResult> GetForecastAsync(int? workerId, int forecastDays, DateTime? fromDate = null, DateTime? toDate = null)
    {
        await EnsureLoadedAsync();

        // Honor the page's date filter when one is set; otherwise use the
        // default 90-day training window.
        var cutoff = (fromDate ?? DateTime.Today.AddDays(-90)).Date;
        var dailyRevenue = _snapshot!.ServiceRecords
            .Where(record => (!workerId.HasValue || record.WorkerId == workerId.Value)
                && record.DatePerformed.Date >= cutoff
                && (!toDate.HasValue || record.DatePerformed.Date <= toDate.Value.Date))
            .GroupBy(record => record.DatePerformed.Date)
            .Select(group => new
            {
                Date = group.Key,
                Revenue = (double)group.Sum(record => record.AmountPaid)
            })
            .OrderBy(item => item.Date)
            .ToList();

        if (dailyRevenue.Count < 2)
        {
            return new ForecastResult([], [], 0, 0, 0);
        }

        var baseDate = dailyRevenue[0].Date;
        var xs = dailyRevenue.Select(item => (double)(item.Date - baseDate).Days).ToArray();
        var ys = dailyRevenue.Select(item => item.Revenue).ToArray();
        var count = xs.Length;

        var sumX = xs.Sum();
        var sumY = ys.Sum();
        var sumXY = xs.Zip(ys, (x, y) => x * y).Sum();
        var sumX2 = xs.Sum(x => x * x);
        var denominator = count * sumX2 - sumX * sumX;

        var slope = denominator == 0 ? 0 : (count * sumXY - sumX * sumY) / denominator;
        var intercept = denominator == 0 ? sumY / count : (sumY - slope * sumX) / count;

        var meanY = sumY / count;
        var ssTotal = ys.Sum(y => (y - meanY) * (y - meanY));
        var ssResidual = xs.Zip(ys, (x, y) =>
        {
            var predicted = slope * x + intercept;
            return (y - predicted) * (y - predicted);
        }).Sum();
        var rSquared = ssTotal > 0 ? 1.0 - ssResidual / ssTotal : 0.0;

        var historical = dailyRevenue.Select(item =>
        {
            var dayIndex = (item.Date - baseDate).Days;
            return new HistoricalPointResult(
                item.Date.ToDateKey(),
                item.Revenue,
                Math.Max(0, slope * dayIndex + intercept));
        }).ToArray();

        var lastDate = dailyRevenue[^1].Date;
        var forecast = Enumerable.Range(1, Math.Max(1, forecastDays)).Select(offset =>
        {
            var futureDate = lastDate.AddDays(offset);
            var dayIndex = (futureDate - baseDate).Days;
            return new ForecastPointResult(
                futureDate.ToDateKey(),
                Math.Max(0, slope * dayIndex + intercept));
        }).ToArray();

        return new ForecastResult(
            historical,
            forecast,
            Math.Round(slope, 2),
            Math.Round(intercept, 2),
            Math.Round(rSquared, 4));
    }

    public async Task<IReadOnlyList<RecentRecordResult>> GetRecentAsync(int count, int? workerId = null)
    {
        await EnsureLoadedAsync();

        var workerNames = _snapshot!.Workers.ToDictionary(worker => worker.Id, worker => worker.Name);
        var serviceNames = _snapshot.Services.ToDictionary(service => service.Id, service => service.Name);

        return _snapshot.ServiceRecords
            .Where(record => !workerId.HasValue || record.WorkerId == workerId.Value)
            .OrderByDescending(record => record.DatePerformed)
            .ThenByDescending(record => record.Id)
            .Take(Math.Max(1, count))
            .Select(record => new RecentRecordResult(
                record.Id,
                record.DatePerformed.ToDateKey(),
                workerNames.GetValueOrDefault(record.WorkerId, "Unknown"),
                serviceNames.GetValueOrDefault(record.ServiceId, "Unknown"),
                record.AmountPaid,
                record.Tips,
                record.ClientName))
            .ToList();
    }

    public async Task<MonthComparisonResult> GetMonthComparisonAsync(int? workerId)
    {
        await EnsureLoadedAsync();

        var today = DateTime.Today;
        var currentStart = new DateTime(today.Year, today.Month, 1);
        var previousStart = currentStart.AddMonths(-1);
        var previousEnd = currentStart.AddDays(-1);

        var currentRecords = FilterStoredRecords(workerId, currentStart, today).ToList();
        var previousRecords = FilterStoredRecords(workerId, previousStart, previousEnd).ToList();

        var currentRevenue = currentRecords.Sum(r => r.AmountPaid);
        var previousRevenue = previousRecords.Sum(r => r.AmountPaid);
        var currentTips = currentRecords.Sum(r => r.Tips);
        var previousTips = previousRecords.Sum(r => r.Tips);
        var currentCount = currentRecords.Count;
        var previousCount = previousRecords.Count;
        var currentAvgTicket = currentCount > 0 ? currentRevenue / currentCount : 0;
        var previousAvgTicket = previousCount > 0 ? previousRevenue / previousCount : 0;

        return new MonthComparisonResult(
            currentRevenue, previousRevenue,
            currentTips, previousTips,
            currentCount, previousCount,
            currentAvgTicket, previousAvgTicket);
    }

    public async Task<IReadOnlyList<DayOfWeekResult>> GetRevenueByDayOfWeekAsync(int? workerId, DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLoadedAsync();

        var dayNames = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var records = FilterStoredRecords(workerId, fromDate, toDate).ToList();

        return dayNames.Select((name, i) =>
        {
            var dow = i == 6 ? DayOfWeek.Sunday : (DayOfWeek)(i + 1);
            var dayRecords = records.Where(r => r.DatePerformed.DayOfWeek == dow).ToList();
            return new DayOfWeekResult(name, dayRecords.Sum(r => r.AmountPaid), dayRecords.Count);
        }).ToList();
    }

    public async Task<IReadOnlyList<TopProductResult>> GetTopProductsAsync(int? workerId, DateTime? fromDate, DateTime? toDate, int count = 5)
    {
        await EnsureLoadedAsync();

        var productNames = _snapshot!.Products.ToDictionary(p => p.Id, p => p.Name);
        var query = _snapshot.ProductSales.AsEnumerable();
        if (workerId.HasValue) query = query.Where(s => s.WorkerId == workerId.Value);
        if (fromDate.HasValue) query = query.Where(s => s.DateSold.Date >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(s => s.DateSold.Date <= toDate.Value.Date);

        return query
            .GroupBy(s => productNames.GetValueOrDefault(s.ProductId, "Unknown"))
            .Select(g => new TopProductResult(g.Key, g.Sum(s => s.Quantity), g.Sum(s => s.UnitPrice * s.Quantity)))
            .OrderByDescending(r => r.Revenue)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public async Task<CombinedTimelineResult> GetCombinedTimelineAsync(int? workerId, DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLoadedAsync();

        var servicesByDay = FilterStoredRecords(workerId, fromDate, toDate)
            .GroupBy(r => r.DatePerformed.Date)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPaid));

        var productQuery = _snapshot!.ProductSales.AsEnumerable();
        if (workerId.HasValue) productQuery = productQuery.Where(s => s.WorkerId == workerId.Value);
        if (fromDate.HasValue) productQuery = productQuery.Where(s => s.DateSold.Date >= fromDate.Value.Date);
        if (toDate.HasValue) productQuery = productQuery.Where(s => s.DateSold.Date <= toDate.Value.Date);
        var productsByDay = productQuery
            .GroupBy(s => s.DateSold.Date)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.UnitPrice * s.Quantity));

        var allDates = servicesByDay.Keys.Union(productsByDay.Keys).OrderBy(d => d).ToList();

        var points = allDates.Select(d => new CombinedTimelinePoint(
            d.ToDateKey(),
            servicesByDay.GetValueOrDefault(d, 0),
            productsByDay.GetValueOrDefault(d, 0)
        )).ToList();

        return new CombinedTimelineResult(points);
    }

    public async Task<ExpenseSummaryResult> GetExpenseSummaryAsync(DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLoadedAsync();

        // Expenses are business-level costs; the worker filter deliberately
        // does not apply here.
        var query = _snapshot!.Expenses.AsEnumerable();
        if (fromDate.HasValue) query = query.Where(expense => expense.Date.Date >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(expense => expense.Date.Date <= toDate.Value.Date);

        var expenses = query.ToList();
        var byCategory = expenses
            .GroupBy(expense => expense.Category)
            .Select(group => new ExpenseCategoryResult(group.Key, group.Sum(expense => expense.Amount), group.Count()))
            .OrderByDescending(item => item.Total)
            .ToList();

        return new ExpenseSummaryResult(expenses.Sum(expense => expense.Amount), byCategory);
    }

    private IEnumerable<ServiceRecord> FilterStoredRecords(int? workerId, DateTime? fromDate, DateTime? toDate)
    {
        var query = _snapshot!.ServiceRecords.AsEnumerable();

        if (workerId.HasValue)
        {
            query = query.Where(record => record.WorkerId == workerId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(record => record.DatePerformed.Date >= fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            query = query.Where(record => record.DatePerformed.Date <= toDate.Value.Date);
        }

        return query;
    }
}
