using Finances.App.Client.Services;
using Finances.App.Client.Tests.TestDoubles;
using Finances.App.Shared;

namespace Finances.App.Client.Tests;

/// <summary>
/// Characterization tests pinning the analytics math in <see cref="LocalFinanceStore"/>.
/// Storage is preloaded with the TestData catalog (workers 1-3, services 1-5).
/// </summary>
public class AnalyticsTests
{
    private static LocalFinanceStore CreateStore() => new(TestData.CreateSeededStorage());

    private static Task<ServiceRecord> AddRecordAsync(
        LocalFinanceStore store,
        DateTime date,
        decimal amount,
        decimal commissionPercent = 50,
        decimal tips = 0,
        int workerId = 1,
        int serviceId = 1)
    {
        return store.AddServiceRecordAsync(new ServiceRecord
        {
            WorkerId = workerId,
            ServiceId = serviceId,
            DatePerformed = date,
            AmountPaid = amount,
            CommissionPercentageApplied = commissionPercent,
            Tips = tips
        });
    }

    [Fact]
    public async Task Forecast_on_perfectly_linear_revenue_recovers_slope_intercept_and_r_squared_of_one()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        await AddRecordAsync(store, today.AddDays(-2), 100);
        await AddRecordAsync(store, today.AddDays(-1), 110);
        await AddRecordAsync(store, today, 120);

        var forecast = await store.GetForecastAsync(null, forecastDays: 2);

        Assert.Equal(10, forecast.Slope);
        Assert.Equal(100, forecast.Intercept);
        Assert.Equal(1.0, forecast.RSquared);

        Assert.Equal(3, forecast.Historical.Length);
        Assert.Equal(100, forecast.Historical[0].Actual);
        Assert.Equal(100, forecast.Historical[0].Trend);
        Assert.Equal(120, forecast.Historical[2].Trend);

        Assert.Equal(2, forecast.Forecast.Length);
        Assert.Equal(130, forecast.Forecast[0].Predicted);
        Assert.Equal(140, forecast.Forecast[1].Predicted);
    }

    [Fact]
    public async Task Forecast_with_fewer_than_two_days_of_data_returns_empty_result()
    {
        var store = CreateStore();
        await AddRecordAsync(store, DateTime.Today, 100);

        var forecast = await store.GetForecastAsync(null, forecastDays: 7);

        Assert.Empty(forecast.Historical);
        Assert.Empty(forecast.Forecast);
        Assert.Equal(0, forecast.Slope);
    }

    [Fact]
    public async Task Forecast_filters_by_worker()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        await AddRecordAsync(store, today.AddDays(-1), 100, workerId: 1);
        await AddRecordAsync(store, today, 200, workerId: 1);
        await AddRecordAsync(store, today, 999, workerId: 2);

        var forecast = await store.GetForecastAsync(workerId: 1, forecastDays: 1);

        Assert.Equal(2, forecast.Historical.Length);
        Assert.Equal(100, forecast.Historical[0].Actual);
        Assert.Equal(200, forecast.Historical[1].Actual);
    }

    [Fact]
    public async Task Revenue_by_day_of_week_maps_monday_first_and_sunday_last()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        var sunday = monday.AddDays(-1);

        await AddRecordAsync(store, monday, 100);
        await AddRecordAsync(store, sunday, 55);

        var byDay = await store.GetRevenueByDayOfWeekAsync(null, null, null);

        Assert.Equal(7, byDay.Count);
        Assert.Equal("Mon", byDay[0].Day);
        Assert.Equal(100, byDay[0].Revenue);
        Assert.Equal(1, byDay[0].Count);
        Assert.Equal("Sun", byDay[6].Day);
        Assert.Equal(55, byDay[6].Revenue);
        Assert.Equal(1, byDay[6].Count);
        Assert.Equal(0, byDay[2].Revenue);
    }

    [Fact]
    public async Task Summary_totals_revenue_tips_and_commission_split()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        await AddRecordAsync(store, today, 100, commissionPercent: 50, tips: 10);
        await AddRecordAsync(store, today, 50, commissionPercent: 45, tips: 5);

        var summary = await store.GetSummaryAsync(null, null, null);

        Assert.Equal(150, summary.TotalRevenue);
        Assert.Equal(15, summary.TotalTips);
        Assert.Equal(72.5m, summary.TotalWorkerShare);
        Assert.Equal(77.5m, summary.TotalSalonShare);
        Assert.Equal(2, summary.RecordCount);
    }

    [Fact]
    public async Task Summary_date_range_is_inclusive_on_both_ends()
    {
        var store = CreateStore();
        var day1 = DateTime.Today.AddDays(-2);
        var day2 = DateTime.Today.AddDays(-1);
        var day3 = DateTime.Today;
        await AddRecordAsync(store, day1, 1);
        await AddRecordAsync(store, day2, 2);
        await AddRecordAsync(store, day3, 4);

        var summary = await store.GetSummaryAsync(null, day1, day2);

        Assert.Equal(3, summary.TotalRevenue);
        Assert.Equal(2, summary.RecordCount);
    }

    [Fact]
    public async Task Month_comparison_separates_current_and_previous_month()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        var currentStart = new DateTime(today.Year, today.Month, 1);
        var previousStart = currentStart.AddMonths(-1);

        await AddRecordAsync(store, currentStart, 200, tips: 20);
        await AddRecordAsync(store, previousStart, 100, tips: 10);

        var comparison = await store.GetMonthComparisonAsync(null);

        Assert.Equal(200, comparison.CurrentRevenue);
        Assert.Equal(100, comparison.PreviousRevenue);
        Assert.Equal(20, comparison.CurrentTips);
        Assert.Equal(10, comparison.PreviousTips);
        Assert.Equal(1, comparison.CurrentCount);
        Assert.Equal(1, comparison.PreviousCount);
        Assert.Equal(200, comparison.CurrentAvgTicket);
        Assert.Equal(100, comparison.PreviousAvgTicket);
    }

    [Fact]
    public async Task Daily_earnings_group_by_day_and_sort_ascending()
    {
        var store = CreateStore();
        var day1 = DateTime.Today.AddDays(-1);
        var day2 = DateTime.Today;
        await AddRecordAsync(store, day2, 30);
        await AddRecordAsync(store, day1, 10, tips: 1);
        await AddRecordAsync(store, day1, 20, tips: 2);

        var earnings = await store.GetDailyEarningsAsync(null, null, null);

        Assert.Equal(2, earnings.Count);
        Assert.Equal(day1.ToDateKey(), earnings[0].Date);
        Assert.Equal(30, earnings[0].Revenue);
        Assert.Equal(3, earnings[0].Tips);
        Assert.Equal(30, earnings[1].Revenue);
    }

    [Fact]
    public async Task Combined_timeline_merges_service_and_product_revenue_by_day()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 10 });
        await AddRecordAsync(store, today, 100);
        await store.AddProductSaleAsync(new ProductSale
        {
            ProductId = 1,
            DateSold = today,
            Quantity = 2,
            UnitPrice = 25
        });

        var timeline = await store.GetCombinedTimelineAsync(null, null, null);

        var point = Assert.Single(timeline.Points);
        Assert.Equal(today.ToDateKey(), point.Date);
        Assert.Equal(100, point.ServiceRevenue);
        Assert.Equal(50, point.ProductRevenue);
    }

    [Fact]
    public async Task Recent_records_honor_the_worker_filter()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        await AddRecordAsync(store, today, 100, workerId: 1);
        await AddRecordAsync(store, today, 200, workerId: 2);

        var all = await store.GetRecentAsync(5);
        var filtered = await store.GetRecentAsync(5, workerId: 2);

        Assert.Equal(2, all.Count);
        Assert.Equal(200, Assert.Single(filtered).AmountPaid);
    }

    [Fact]
    public async Task Top_products_rank_by_revenue()
    {
        var store = CreateStore();
        var today = DateTime.Today;
        await store.AddProductAsync(new Product { Name = "Pomade", Price = 25, StockQuantity = 100 });
        await store.AddProductAsync(new Product { Name = "Shampoo", Price = 10, StockQuantity = 100 });
        await store.AddProductSaleAsync(new ProductSale { ProductId = 1, DateSold = today, Quantity = 1, UnitPrice = 25 });
        await store.AddProductSaleAsync(new ProductSale { ProductId = 2, DateSold = today, Quantity = 5, UnitPrice = 10 });

        var top = await store.GetTopProductsAsync(null, null, null);

        Assert.Equal(2, top.Count);
        Assert.Equal("Shampoo", top[0].Product);
        Assert.Equal(50, top[0].Revenue);
        Assert.Equal("Pomade", top[1].Product);
    }
}
