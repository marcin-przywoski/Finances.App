using System.Globalization;
using Finances.App.Client.Services;
using Finances.App.Client.Tests.TestDoubles;
using Finances.App.Shared;

namespace Finances.App.Client.Tests;

/// <summary>
/// Phase 3 tests: rounding policy for commission splits and invariant date keys.
/// </summary>
public class MoneyAndDateTests
{
    [Theory]
    [InlineData(33.335, 50, 16.67)]   // midpoint rounds away from zero
    [InlineData(100, 33, 33)]
    [InlineData(9.99, 35, 3.50)]      // 3.4965 -> 3.50
    [InlineData(0.01, 50, 0.01)]      // 0.005 -> 0.01
    public void Worker_share_is_rounded_to_cents(decimal amount, decimal commission, decimal expected)
    {
        var record = new ServiceRecord { AmountPaid = amount, CommissionPercentageApplied = commission };

        Assert.Equal(expected, record.WorkerShare);
    }

    [Theory]
    [InlineData(33.335, 50)]
    [InlineData(9.99, 35)]
    [InlineData(77.77, 33)]
    [InlineData(0.01, 50)]
    public void Worker_and_salon_share_always_sum_to_amount_paid(decimal amount, decimal commission)
    {
        var record = new ServiceRecord { AmountPaid = amount, CommissionPercentageApplied = commission };

        Assert.Equal(amount, record.WorkerShare + record.SalonShare);
    }

    [Fact]
    public async Task Summary_worker_share_equals_sum_of_displayed_row_shares()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        // Values chosen so unrounded shares would NOT sum to the rounded rows.
        var amounts = new[] { 9.99m, 33.335m, 77.77m };
        foreach (var amount in amounts)
        {
            await store.AddServiceRecordAsync(new ServiceRecord
            {
                WorkerId = 1,
                ServiceId = 1,
                DatePerformed = DateTime.Today,
                AmountPaid = amount,
                CommissionPercentageApplied = 33
            });
        }

        var summary = await store.GetSummaryAsync(null, null, null);
        var expectedRowSum = amounts
            .Select(amount => new ServiceRecord { AmountPaid = amount, CommissionPercentageApplied = 33 }.WorkerShare)
            .Sum();

        Assert.Equal(expectedRowSum, summary.TotalWorkerShare);
        Assert.Equal(summary.TotalRevenue - expectedRowSum, summary.TotalSalonShare);
    }

    [Fact]
    public void Date_key_is_invariant_regardless_of_thread_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // Thai culture uses the Buddhist calendar: 2026 formats as 2569
            // with plain ToString("yyyy-MM-dd").
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var date = new DateTime(2026, 8, 1);

            Assert.Equal("2026-08-01", date.ToDateKey());
            Assert.NotEqual("2026-08-01", date.ToString("yyyy-MM-dd"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Forecast_honors_date_range_filter()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var today = DateTime.Today;
        // Older data outside the filtered window.
        await store.AddServiceRecordAsync(new ServiceRecord { WorkerId = 1, ServiceId = 1, DatePerformed = today.AddDays(-10), AmountPaid = 999, CommissionPercentageApplied = 50 });
        // In-window linear data.
        await store.AddServiceRecordAsync(new ServiceRecord { WorkerId = 1, ServiceId = 1, DatePerformed = today.AddDays(-1), AmountPaid = 100, CommissionPercentageApplied = 50 });
        await store.AddServiceRecordAsync(new ServiceRecord { WorkerId = 1, ServiceId = 1, DatePerformed = today, AmountPaid = 110, CommissionPercentageApplied = 50 });

        var filtered = await store.GetForecastAsync(null, 1, from: today.AddDays(-2), to: today);

        Assert.Equal(2, filtered.Historical.Length);
        Assert.Equal(100, filtered.Historical[0].Actual);
        Assert.Equal(10, filtered.Slope);
    }

    [Fact]
    public async Task Forecast_defaults_to_ninety_day_window_when_no_filter_given()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());
        var today = DateTime.Today;
        await store.AddServiceRecordAsync(new ServiceRecord { WorkerId = 1, ServiceId = 1, DatePerformed = today.AddDays(-120), AmountPaid = 999, CommissionPercentageApplied = 50 });
        await store.AddServiceRecordAsync(new ServiceRecord { WorkerId = 1, ServiceId = 1, DatePerformed = today.AddDays(-1), AmountPaid = 100, CommissionPercentageApplied = 50 });
        await store.AddServiceRecordAsync(new ServiceRecord { WorkerId = 1, ServiceId = 1, DatePerformed = today, AmountPaid = 110, CommissionPercentageApplied = 50 });

        var forecast = await store.GetForecastAsync(null, 1);

        // The 120-day-old record falls outside the default window.
        Assert.Equal(2, forecast.Historical.Length);
    }
}
