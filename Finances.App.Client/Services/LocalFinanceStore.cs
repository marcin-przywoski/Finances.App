using System.Text.Json;
using Finances.App.Client.Models;
using Finances.App.Client.Services.Storage;
using Finances.App.Shared;

namespace Finances.App.Client.Services;

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

public enum SnapshotLoadStatus
{
    Ok,
    RecoveredFromCorruptData
}

public sealed class LocalFinanceStore
{
    private const int CurrentSchemaVersion = 1;
    public const string StorageKey = "Finances.App.snapshot";
    public const string QuarantineKey = "Finances.App.snapshot.quarantine";
    public const string PreResetKey = "Finances.App.snapshot.pre-reset";
    public const string RevisionKey = "Finances.App.snapshot.rev";

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        TypeInfoResolver = FinanceJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        TypeInfoResolver = FinanceJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IKeyValueStorage _storage;

    private FinanceSnapshot? _snapshot;
    private Task? _loadTask;
    private string? _loadedRevision;

    public event Action? OnChange;

    public SnapshotLoadStatus LoadStatus { get; private set; } = SnapshotLoadStatus.Ok;

    public LocalFinanceStore(IKeyValueStorage storage)
    {
        _storage = storage;
    }

    public async Task<SnapshotLoadStatus> GetLoadStatusAsync()
    {
        await EnsureLoadedAsync();
        return LoadStatus;
    }

    /// <summary>
    /// Called when another tab wrote to the snapshot key. Drops the cached
    /// snapshot so the next read reloads the newest persisted data.
    /// </summary>
    public void HandleExternalDataChange()
    {
        _snapshot = null;
        _loadTask = null;
        NotifyChanged();
    }

    public ValueTask<string?> GetQuarantinedJsonAsync()
    {
        return _storage.GetItemAsync(QuarantineKey);
    }

    public async Task ClearQuarantineAsync()
    {
        await _storage.RemoveItemAsync(QuarantineKey);
        LoadStatus = SnapshotLoadStatus.Ok;
        NotifyChanged();
    }

    public async Task<IReadOnlyList<Worker>> GetWorkersAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.Workers.OrderBy(worker => worker.Name, StringComparer.CurrentCultureIgnoreCase).Select(CloneWorker).ToList();
    }

    public async Task<IReadOnlyList<Service>> GetServicesAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.Services.OrderBy(service => service.Name, StringComparer.CurrentCultureIgnoreCase).Select(CloneService).ToList();
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.Products.OrderBy(product => product.Name, StringComparer.CurrentCultureIgnoreCase).Select(CloneProduct).ToList();
    }

    public async Task<IReadOnlyList<ServiceRecord>> GetServiceRecordsAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.ServiceRecords
            .OrderByDescending(record => record.DatePerformed)
            .ThenByDescending(record => record.Id)
            .Select(EnrichRecord)
            .ToList();
    }

    public async Task<Worker> AddWorkerAsync(Worker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);

        await EnsureLoadedAsync();

        var created = CloneWorker(worker);
        created.Id = _snapshot!.NextWorkerId++;
        created.Name = created.Name.Trim();
        created.ApplicationUserId = NormalizeOptionalText(created.ApplicationUserId);

        _snapshot.Workers.Add(created);
        await PersistAsync();

        return CloneWorker(created);
    }

    public async Task UpdateWorkerAsync(int id, Worker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        await EnsureLoadedAsync();

        if (id != worker.Id)
        {
            throw new InvalidDataException("Worker identifier mismatch.");
        }

        var existing = _snapshot!.Workers.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        existing.Name = worker.Name.Trim();
        existing.DefaultCommissionPercentage = worker.DefaultCommissionPercentage;
        existing.ApplicationUserId = NormalizeOptionalText(worker.ApplicationUserId);

        await PersistAsync();
    }

    public async Task<DeleteResult> DeleteWorkerAsync(int id)
    {
        await EnsureLoadedAsync();

        var worker = _snapshot!.Workers.FirstOrDefault(item => item.Id == id);
        if (worker is null)
        {
            throw new KeyNotFoundException();
        }

        if (_snapshot.ServiceRecords.Any(record => record.WorkerId == id))
        {
            return new DeleteResult(false, "Cannot delete worker with existing service records.");
        }

        _snapshot.Workers.Remove(worker);
        await PersistAsync();
        return new DeleteResult(true);
    }

    public async Task<Service> AddServiceAsync(Service service)
    {
        ArgumentNullException.ThrowIfNull(service);

        await EnsureLoadedAsync();

        var created = CloneService(service);
        created.Id = _snapshot!.NextServiceId++;
        created.Name = created.Name.Trim();

        _snapshot.Services.Add(created);
        await PersistAsync();

        return CloneService(created);
    }

    public async Task UpdateServiceAsync(int id, Service service)
    {
        ArgumentNullException.ThrowIfNull(service);
        await EnsureLoadedAsync();

        if (id != service.Id)
        {
            throw new InvalidDataException("Service identifier mismatch.");
        }

        var existing = _snapshot!.Services.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        existing.Name = service.Name.Trim();
        existing.BasePrice = service.BasePrice;

        await PersistAsync();
    }

    public async Task<DeleteResult> DeleteServiceAsync(int id)
    {
        await EnsureLoadedAsync();

        var service = _snapshot!.Services.FirstOrDefault(item => item.Id == id);
        if (service is null)
        {
            throw new KeyNotFoundException();
        }

        if (_snapshot.ServiceRecords.Any(record => record.ServiceId == id))
        {
            return new DeleteResult(false, "Cannot delete service with existing service records.");
        }

        _snapshot.Services.Remove(service);
        await PersistAsync();
        return new DeleteResult(true);
    }

    public async Task<Product> AddProductAsync(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        await EnsureLoadedAsync();

        var created = CloneProduct(product);
        created.Id = _snapshot!.NextProductId++;
        created.Name = created.Name.Trim();
        created.Category = NormalizeOptionalText(created.Category);

        _snapshot.Products.Add(created);
        await PersistAsync();

        return CloneProduct(created);
    }

    public async Task UpdateProductAsync(int id, Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        await EnsureLoadedAsync();

        if (id != product.Id)
        {
            throw new InvalidDataException("Product identifier mismatch.");
        }

        var existing = _snapshot!.Products.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        existing.Name = product.Name.Trim();
        existing.Price = product.Price;
        existing.StockQuantity = product.StockQuantity;
        existing.Category = NormalizeOptionalText(product.Category);

        await PersistAsync();
    }

    public async Task DeleteProductAsync(int id)
    {
        await EnsureLoadedAsync();

        var product = _snapshot!.Products.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        _snapshot.Products.Remove(product);
        await PersistAsync();
    }

    public async Task<IReadOnlyList<ProductSale>> GetProductSalesAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.ProductSales
            .OrderByDescending(sale => sale.DateSold)
            .ThenByDescending(sale => sale.Id)
            .Select(EnrichProductSale)
            .ToList();
    }

    public async Task<ProductSale> AddProductSaleAsync(ProductSale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        await EnsureLoadedAsync();

        var product = _snapshot!.Products.FirstOrDefault(item => item.Id == sale.ProductId)
            ?? throw new InvalidDataException("The selected product does not exist.");

        if (sale.WorkerId.HasValue && _snapshot.Workers.All(worker => worker.Id != sale.WorkerId.Value))
        {
            throw new InvalidDataException("The selected worker does not exist.");
        }

        if (product.StockQuantity < sale.Quantity)
        {
            throw new InvalidDataException($"Only {product.StockQuantity} of \"{product.Name}\" in stock — cannot sell {sale.Quantity}.");
        }

        var stored = new ProductSale
        {
            Id = _snapshot.NextProductSaleId++,
            ProductId = product.Id,
            WorkerId = sale.WorkerId == 0 ? null : sale.WorkerId,
            DateSold = sale.DateSold == default ? DateTime.Today : sale.DateSold.Date,
            Quantity = sale.Quantity,
            UnitPrice = sale.UnitPrice,
            ClientName = NormalizeOptionalText(sale.ClientName),
            Notes = NormalizeOptionalText(sale.Notes)
        };

        _snapshot.ProductSales.Add(stored);
        product.StockQuantity -= stored.Quantity;

        var enriched = EnrichProductSale(stored);
        await PersistAsync();
        return enriched;
    }

    public async Task UpdateProductSaleAsync(int id, ProductSale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        await EnsureLoadedAsync();

        if (id != sale.Id)
        {
            throw new InvalidDataException("Sale identifier mismatch.");
        }

        var existing = _snapshot!.ProductSales.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();

        var newProduct = _snapshot.Products.FirstOrDefault(product => product.Id == sale.ProductId)
            ?? throw new InvalidDataException("The selected product does not exist.");

        if (sale.WorkerId.HasValue && sale.WorkerId.Value != 0 && _snapshot.Workers.All(worker => worker.Id != sale.WorkerId.Value))
        {
            throw new InvalidDataException("The selected worker does not exist.");
        }

        // Stock check before any mutation: the old quantity returns to the old
        // product, so when the product is unchanged it counts as available.
        var oldProduct = _snapshot.Products.FirstOrDefault(product => product.Id == existing.ProductId);
        var available = newProduct.StockQuantity + (ReferenceEquals(oldProduct, newProduct) ? existing.Quantity : 0);
        if (available < sale.Quantity)
        {
            throw new InvalidDataException($"Only {available} of \"{newProduct.Name}\" in stock — cannot sell {sale.Quantity}.");
        }

        if (oldProduct is not null)
        {
            oldProduct.StockQuantity += existing.Quantity;
        }
        newProduct.StockQuantity -= sale.Quantity;

        existing.ProductId = sale.ProductId;
        existing.WorkerId = sale.WorkerId == 0 ? null : sale.WorkerId;
        existing.DateSold = sale.DateSold == default ? DateTime.Today : sale.DateSold.Date;
        existing.Quantity = sale.Quantity;
        existing.UnitPrice = sale.UnitPrice;
        existing.ClientName = NormalizeOptionalText(sale.ClientName);
        existing.Notes = NormalizeOptionalText(sale.Notes);

        await PersistAsync();
    }

    public async Task DeleteProductSaleAsync(int id)
    {
        await EnsureLoadedAsync();

        var sale = _snapshot!.ProductSales.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        _snapshot.ProductSales.Remove(sale);

        // Deleting a sale returns its units to stock (if the product still exists).
        var product = _snapshot.Products.FirstOrDefault(item => item.Id == sale.ProductId);
        if (product is not null)
        {
            product.StockQuantity += sale.Quantity;
        }

        await PersistAsync();
    }

    public async Task<decimal> GetProductSalesRevenueAsync(DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var query = _snapshot!.ProductSales.AsEnumerable();
        if (from.HasValue) query = query.Where(s => s.DateSold.Date >= from.Value.Date);
        if (to.HasValue) query = query.Where(s => s.DateSold.Date <= to.Value.Date);

        return query.Sum(s => s.UnitPrice * s.Quantity);
    }

    public async Task<ServiceRecord> AddServiceRecordAsync(ServiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        await EnsureLoadedAsync();

        var worker = _snapshot!.Workers.FirstOrDefault(item => item.Id == record.WorkerId);
        var service = _snapshot.Services.FirstOrDefault(item => item.Id == record.ServiceId);

        if (worker is null || service is null)
        {
            throw new InvalidDataException("The selected worker or service does not exist.");
        }

        var stored = new ServiceRecord
        {
            Id = _snapshot.NextServiceRecordId++,
            WorkerId = worker.Id,
            ServiceId = service.Id,
            DatePerformed = record.DatePerformed == default ? DateTime.Today : record.DatePerformed.Date,
            AmountPaid = record.AmountPaid,
            CommissionPercentageApplied = record.CommissionPercentageApplied,
            Tips = record.Tips,
            ClientName = NormalizeOptionalText(record.ClientName),
            Notes = NormalizeOptionalText(record.Notes)
        };

        _snapshot.ServiceRecords.Add(stored);

        var enriched = EnrichRecord(stored);
        await PersistAsync();
        return enriched;
    }

    public async Task UpdateServiceRecordAsync(int id, ServiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        await EnsureLoadedAsync();

        if (id != record.Id)
        {
            throw new InvalidDataException("Record identifier mismatch.");
        }

        var existing = _snapshot!.ServiceRecords.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();

        if (_snapshot.Workers.All(worker => worker.Id != record.WorkerId) || _snapshot.Services.All(service => service.Id != record.ServiceId))
        {
            throw new InvalidDataException("The selected worker or service does not exist.");
        }

        existing.WorkerId = record.WorkerId;
        existing.ServiceId = record.ServiceId;
        existing.DatePerformed = record.DatePerformed == default ? DateTime.Today : record.DatePerformed.Date;
        existing.AmountPaid = record.AmountPaid;
        existing.CommissionPercentageApplied = record.CommissionPercentageApplied;
        existing.Tips = record.Tips;
        existing.ClientName = NormalizeOptionalText(record.ClientName);
        existing.Notes = NormalizeOptionalText(record.Notes);

        await PersistAsync();
    }

    public async Task DeleteServiceRecordAsync(int id)
    {
        await EnsureLoadedAsync();

        var record = _snapshot!.ServiceRecords.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        _snapshot.ServiceRecords.Remove(record);
        await PersistAsync();
    }

    public async Task<SummaryResult> GetSummaryAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var records = FilterStoredRecords(workerId, from, to).ToList();
        var totalRevenue = records.Sum(record => record.AmountPaid);
        var totalTips = records.Sum(record => record.Tips);
        var totalWorkerShare = records.Sum(record => record.AmountPaid * (record.CommissionPercentageApplied / 100));
        var totalSalonShare = totalRevenue - totalWorkerShare;

        return new SummaryResult(totalRevenue, totalTips, totalWorkerShare, totalSalonShare, records.Count);
    }

    public async Task<IReadOnlyList<DailyEarningResult>> GetDailyEarningsAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        return FilterStoredRecords(workerId, from, to)
            .GroupBy(record => record.DatePerformed.Date)
            .Select(group => new DailyEarningResult(
                group.Key.ToString("yyyy-MM-dd"),
                group.Sum(record => record.AmountPaid),
                group.Sum(record => record.Tips)))
            .OrderBy(item => item.Date, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<WorkerRevenueResult>> GetRevenueByWorkerAsync(DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var workerNames = _snapshot!.Workers.ToDictionary(worker => worker.Id, worker => worker.Name);

        return FilterStoredRecords(null, from, to)
            .GroupBy(record => workerNames.GetValueOrDefault(record.WorkerId, "Unknown"))
            .Select(group => new WorkerRevenueResult(
                group.Key,
                group.Sum(record => record.AmountPaid),
                group.Sum(record => record.Tips)))
            .OrderByDescending(item => item.Revenue)
            .ToList();
    }

    public async Task<IReadOnlyList<ServicePopularityResult>> GetServicePopularityAsync(DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var serviceNames = _snapshot!.Services.ToDictionary(service => service.Id, service => service.Name);

        return FilterStoredRecords(null, from, to)
            .GroupBy(record => serviceNames.GetValueOrDefault(record.ServiceId, "Unknown"))
            .Select(group => new ServicePopularityResult(
                group.Key,
                group.Count(),
                group.Sum(record => record.AmountPaid)))
            .OrderByDescending(item => item.Count)
            .ToList();
    }

    public async Task<ForecastResult> GetForecastAsync(int? workerId, int forecastDays)
    {
        await EnsureLoadedAsync();

        var cutoff = DateTime.Today.AddDays(-90);
        var dailyRevenue = _snapshot!.ServiceRecords
            .Where(record => (!workerId.HasValue || record.WorkerId == workerId.Value) && record.DatePerformed.Date >= cutoff)
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
                item.Date.ToString("yyyy-MM-dd"),
                item.Revenue,
                Math.Max(0, slope * dayIndex + intercept));
        }).ToArray();

        var lastDate = dailyRevenue[^1].Date;
        var forecast = Enumerable.Range(1, Math.Max(1, forecastDays)).Select(offset =>
        {
            var futureDate = lastDate.AddDays(offset);
            var dayIndex = (futureDate - baseDate).Days;
            return new ForecastPointResult(
                futureDate.ToString("yyyy-MM-dd"),
                Math.Max(0, slope * dayIndex + intercept));
        }).ToArray();

        return new ForecastResult(
            historical,
            forecast,
            Math.Round(slope, 2),
            Math.Round(intercept, 2),
            Math.Round(rSquared, 4));
    }

    public async Task<IReadOnlyList<RecentRecordResult>> GetRecentAsync(int count)
    {
        await EnsureLoadedAsync();

        var workerNames = _snapshot!.Workers.ToDictionary(worker => worker.Id, worker => worker.Name);
        var serviceNames = _snapshot.Services.ToDictionary(service => service.Id, service => service.Name);

        return _snapshot.ServiceRecords
            .OrderByDescending(record => record.DatePerformed)
            .ThenByDescending(record => record.Id)
            .Take(Math.Max(1, count))
            .Select(record => new RecentRecordResult(
                record.Id,
                record.DatePerformed.ToString("yyyy-MM-dd"),
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

    public async Task<IReadOnlyList<DayOfWeekResult>> GetRevenueByDayOfWeekAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var dayNames = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var records = FilterStoredRecords(workerId, from, to).ToList();

        return dayNames.Select((name, i) =>
        {
            var dow = i == 6 ? DayOfWeek.Sunday : (DayOfWeek)(i + 1);
            var dayRecords = records.Where(r => r.DatePerformed.DayOfWeek == dow).ToList();
            return new DayOfWeekResult(name, dayRecords.Sum(r => r.AmountPaid), dayRecords.Count);
        }).ToList();
    }

    public async Task<IReadOnlyList<TopProductResult>> GetTopProductsAsync(DateTime? from, DateTime? to, int count = 5)
    {
        await EnsureLoadedAsync();

        var productNames = _snapshot!.Products.ToDictionary(p => p.Id, p => p.Name);
        var query = _snapshot.ProductSales.AsEnumerable();
        if (from.HasValue) query = query.Where(s => s.DateSold.Date >= from.Value.Date);
        if (to.HasValue) query = query.Where(s => s.DateSold.Date <= to.Value.Date);

        return query
            .GroupBy(s => productNames.GetValueOrDefault(s.ProductId, "Unknown"))
            .Select(g => new TopProductResult(g.Key, g.Sum(s => s.Quantity), g.Sum(s => s.UnitPrice * s.Quantity)))
            .OrderByDescending(r => r.Revenue)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public async Task<CombinedTimelineResult> GetCombinedTimelineAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var servicesByDay = FilterStoredRecords(workerId, from, to)
            .GroupBy(r => r.DatePerformed.Date)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPaid));

        var productQuery = _snapshot!.ProductSales.AsEnumerable();
        if (from.HasValue) productQuery = productQuery.Where(s => s.DateSold.Date >= from.Value.Date);
        if (to.HasValue) productQuery = productQuery.Where(s => s.DateSold.Date <= to.Value.Date);
        var productsByDay = productQuery
            .GroupBy(s => s.DateSold.Date)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.UnitPrice * s.Quantity));

        var allDates = servicesByDay.Keys.Union(productsByDay.Keys).OrderBy(d => d).ToList();

        var points = allDates.Select(d => new CombinedTimelinePoint(
            d.ToString("yyyy-MM-dd"),
            servicesByDay.GetValueOrDefault(d, 0),
            productsByDay.GetValueOrDefault(d, 0)
        )).ToList();

        return new CombinedTimelineResult(points);
    }

    public async Task<DataStateSummary> GetDataStateSummaryAsync()
    {
        await EnsureLoadedAsync();
        return new DataStateSummary(
            _snapshot!.Workers.Count,
            _snapshot.Services.Count,
            _snapshot.Products.Count,
            _snapshot.ServiceRecords.Count,
            _snapshot.ProductSales.Count,
            _snapshot.SchemaVersion,
            _snapshot.LastUpdatedUtc,
            JsonSerializer.Serialize(_snapshot, CompactJson).Length);
    }

    public async Task<string> ExportAsync()
    {
        await EnsureLoadedAsync();
        return JsonSerializer.Serialize(_snapshot, IndentedJson);
    }

    public async Task ImportAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The selected file is empty.");
        }

        FinanceSnapshot? imported;

        try
        {
            imported = JsonSerializer.Deserialize<FinanceSnapshot>(json, CompactJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The selected file is not a valid backup.", ex);
        }

        if (imported is null)
        {
            throw new InvalidDataException("The selected file does not contain finance data.");
        }

        MigrateSnapshot(imported);
        NormalizeSnapshot(imported);
        ValidateSnapshot(imported);

        await EnsureLoadedAsync();

        _snapshot = imported;
        _snapshot.LastUpdatedUtc = DateTime.UtcNow;

        await SaveAsync();
        NotifyChanged();
    }

    public async Task ResetAsync()
    {
        await EnsureLoadedAsync();

        try
        {
            // Cheap undo: keep the outgoing data under a side key.
            var outgoing = JsonSerializer.Serialize(_snapshot, IndentedJson);
            await _storage.SetItemAsync(PreResetKey, outgoing);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not write pre-reset backup: {ex.Message}");
        }

        _snapshot = CreateDefaultSnapshot();
        LoadStatus = SnapshotLoadStatus.Ok;
        await SaveAsync();
        NotifyChanged();
    }

    private Task EnsureLoadedAsync()
    {
        // Cached-task latch: concurrent callers (e.g. the worker selector in the
        // layout and the routed page) await one load instead of racing two.
        if (_loadTask is null || _loadTask.IsFaulted || _loadTask.IsCanceled)
        {
            _loadTask = LoadCoreAsync();
        }

        return _loadTask;
    }

    private async Task LoadCoreAsync()
    {
        var json = await _storage.GetItemAsync(StorageKey);
        _loadedRevision = await _storage.GetItemAsync(RevisionKey);

        if (string.IsNullOrWhiteSpace(json))
        {
            _snapshot = CreateDefaultSnapshot();
            LoadStatus = SnapshotLoadStatus.Ok;
            await SaveAsync();
            return;
        }

        FinanceSnapshot? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<FinanceSnapshot>(json, CompactJson);
        }
        catch (JsonException)
        {
            loaded = null;
        }

        if (loaded is not null)
        {
            try
            {
                MigrateSnapshot(loaded);
                NormalizeSnapshot(loaded);
                ValidateSnapshot(loaded);
            }
            catch (InvalidDataException)
            {
                loaded = null;
            }
        }

        if (loaded is null)
        {
            // Never overwrite unreadable data: preserve the original bytes
            // under a quarantine key, then start from defaults and tell the UI.
            await _storage.SetItemAsync(QuarantineKey, json);
            _snapshot = CreateDefaultSnapshot();
            LoadStatus = SnapshotLoadStatus.RecoveredFromCorruptData;
            await SaveAsync();
            NotifyChanged();
            return;
        }

        _snapshot = loaded;
        LoadStatus = SnapshotLoadStatus.Ok;
    }

    private async Task PersistAsync()
    {
        await AssertNotChangedInAnotherTabAsync();
        _snapshot!.LastUpdatedUtc = DateTime.UtcNow;
        await SaveAsync();
        NotifyChanged();
    }

    private async Task AssertNotChangedInAnotherTabAsync()
    {
        var currentRevision = await _storage.GetItemAsync(RevisionKey);
        if (!string.Equals(currentRevision, _loadedRevision, StringComparison.Ordinal))
        {
            HandleExternalDataChange();
            throw new ConcurrentUpdateException();
        }
    }

    private async Task SaveAsync()
    {
        _snapshot!.SchemaVersion = CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(_snapshot, CompactJson);

        try
        {
            await _storage.SetItemAsync(StorageKey, json);
            var revision = Guid.NewGuid().ToString("N");
            await _storage.SetItemAsync(RevisionKey, revision);
            _loadedRevision = revision;
        }
        catch (Exception ex) when (ex is not ConcurrentUpdateException)
        {
            // Discard the unsaved in-memory state so the next read reloads the
            // last successfully persisted snapshot; the UI re-renders that.
            _snapshot = null;
            _loadTask = null;
            throw new StorageWriteException(ex);
        }
    }

    private void NotifyChanged()
    {
        OnChange?.Invoke();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Worker CloneWorker(Worker worker)
    {
        return new Worker
        {
            Id = worker.Id,
            Name = worker.Name,
            DefaultCommissionPercentage = worker.DefaultCommissionPercentage,
            ApplicationUserId = worker.ApplicationUserId
        };
    }

    private static Service CloneService(Service service)
    {
        return new Service
        {
            Id = service.Id,
            Name = service.Name,
            BasePrice = service.BasePrice
        };
    }

    private static Product CloneProduct(Product product)
    {
        return new Product
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            Category = product.Category
        };
    }

    private ServiceRecord EnrichRecord(ServiceRecord record)
    {
        return new ServiceRecord
        {
            Id = record.Id,
            WorkerId = record.WorkerId,
            Worker = _snapshot!.Workers.Where(worker => worker.Id == record.WorkerId).Select(CloneWorker).FirstOrDefault(),
            ServiceId = record.ServiceId,
            Service = _snapshot.Services.Where(service => service.Id == record.ServiceId).Select(CloneService).FirstOrDefault(),
            DatePerformed = record.DatePerformed,
            AmountPaid = record.AmountPaid,
            CommissionPercentageApplied = record.CommissionPercentageApplied,
            Tips = record.Tips,
            ClientName = record.ClientName,
            Notes = record.Notes
        };
    }

    private ProductSale EnrichProductSale(ProductSale sale)
    {
        return new ProductSale
        {
            Id = sale.Id,
            ProductId = sale.ProductId,
            Product = _snapshot!.Products.Where(p => p.Id == sale.ProductId).Select(CloneProduct).FirstOrDefault(),
            WorkerId = sale.WorkerId,
            Worker = sale.WorkerId.HasValue
                ? _snapshot.Workers.Where(w => w.Id == sale.WorkerId.Value).Select(CloneWorker).FirstOrDefault()
                : null,
            DateSold = sale.DateSold,
            Quantity = sale.Quantity,
            UnitPrice = sale.UnitPrice,
            ClientName = sale.ClientName,
            Notes = sale.Notes
        };
    }

    private IEnumerable<ServiceRecord> FilterStoredRecords(int? workerId, DateTime? from, DateTime? to)
    {
        var query = _snapshot!.ServiceRecords.AsEnumerable();

        if (workerId.HasValue)
        {
            query = query.Where(record => record.WorkerId == workerId.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(record => record.DatePerformed.Date >= from.Value.Date);
        }

        if (to.HasValue)
        {
            query = query.Where(record => record.DatePerformed.Date <= to.Value.Date);
        }

        return query;
    }

    private static FinanceSnapshot CreateDefaultSnapshot()
    {
        var snapshot = new FinanceSnapshot
        {
            Workers =
            [
                new Worker { Id = 1, Name = "Jan Kowalski", DefaultCommissionPercentage = 50 },
                new Worker { Id = 2, Name = "Anna Nowak", DefaultCommissionPercentage = 45 },
                new Worker { Id = 3, Name = "Piotr Wisniewski", DefaultCommissionPercentage = 55 }
            ],
            Services =
            [
                new Service { Id = 1, Name = "Strzyzenie meskie", BasePrice = 50 },
                new Service { Id = 2, Name = "Strzyzenie damskie", BasePrice = 80 },
                new Service { Id = 3, Name = "Broda", BasePrice = 30 },
                new Service { Id = 4, Name = "Koloryzacja", BasePrice = 150 },
                new Service { Id = 5, Name = "Strzyzenie + Broda", BasePrice = 70 }
            ],
            Products = [],
            ServiceRecords = []
        };

        NormalizeSnapshot(snapshot);
        return snapshot;
    }

    private static void NormalizeSnapshot(FinanceSnapshot snapshot)
    {
        snapshot.Workers ??= [];
        snapshot.Services ??= [];
        snapshot.Products ??= [];
        snapshot.ServiceRecords ??= [];

        foreach (var worker in snapshot.Workers)
        {
            worker.Name = (worker.Name ?? string.Empty).Trim();
            worker.ApplicationUserId = NormalizeOptionalText(worker.ApplicationUserId);
        }

        foreach (var service in snapshot.Services)
        {
            service.Name = (service.Name ?? string.Empty).Trim();
        }

        foreach (var product in snapshot.Products)
        {
            product.Name = (product.Name ?? string.Empty).Trim();
            product.Category = NormalizeOptionalText(product.Category);
        }

        foreach (var record in snapshot.ServiceRecords)
        {
            record.Worker = null;
            record.Service = null;
            record.ClientName = NormalizeOptionalText(record.ClientName);
            record.Notes = NormalizeOptionalText(record.Notes);
            record.DatePerformed = record.DatePerformed == default ? DateTime.Today : record.DatePerformed.Date;
        }

        snapshot.ProductSales ??= [];

        foreach (var sale in snapshot.ProductSales)
        {
            sale.Product = null;
            sale.Worker = null;
            sale.ClientName = NormalizeOptionalText(sale.ClientName);
            sale.Notes = NormalizeOptionalText(sale.Notes);
            sale.DateSold = sale.DateSold == default ? DateTime.Today : sale.DateSold.Date;
        }

        snapshot.NextWorkerId = Math.Max(snapshot.NextWorkerId, snapshot.Workers.Select(worker => worker.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextServiceId = Math.Max(snapshot.NextServiceId, snapshot.Services.Select(service => service.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextProductId = Math.Max(snapshot.NextProductId, snapshot.Products.Select(product => product.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextServiceRecordId = Math.Max(snapshot.NextServiceRecordId, snapshot.ServiceRecords.Select(record => record.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextProductSaleId = Math.Max(snapshot.NextProductSaleId, snapshot.ProductSales.Select(sale => sale.Id).DefaultIfEmpty().Max() + 1);
        snapshot.LastUpdatedUtc = snapshot.LastUpdatedUtc == default ? DateTime.UtcNow : snapshot.LastUpdatedUtc;
    }

    private static void MigrateSnapshot(FinanceSnapshot snapshot)
    {
        if (snapshot.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"This backup was created by a newer version of the app (schema v{snapshot.SchemaVersion}; this app supports up to v{CurrentSchemaVersion}). Update the app, then import again.");
        }

        if (snapshot.SchemaVersion <= 0)
        {
            snapshot.SchemaVersion = 1;
        }

        // Per-version transforms go here as the schema evolves, e.g.:
        // if (snapshot.SchemaVersion == 1) { ...upgrade to v2...; snapshot.SchemaVersion = 2; }

        snapshot.SchemaVersion = CurrentSchemaVersion;
    }

    private static void ValidateSnapshot(FinanceSnapshot snapshot)
    {
        EnsureDistinctIds(snapshot.Workers, worker => worker.Id, "workers");
        EnsureDistinctIds(snapshot.Services, service => service.Id, "services");
        EnsureDistinctIds(snapshot.Products, product => product.Id, "products");
        EnsureDistinctIds(snapshot.ServiceRecords, record => record.Id, "service records");
        EnsureDistinctIds(snapshot.ProductSales, sale => sale.Id, "product sales");

        var workerIds = snapshot.Workers.Select(worker => worker.Id).ToHashSet();
        var serviceIds = snapshot.Services.Select(service => service.Id).ToHashSet();
        var productIds = snapshot.Products.Select(product => product.Id).ToHashSet();

        if (snapshot.ServiceRecords.Any(record => !workerIds.Contains(record.WorkerId)))
        {
            throw new InvalidDataException("The backup references a worker that does not exist.");
        }

        if (snapshot.ServiceRecords.Any(record => !serviceIds.Contains(record.ServiceId)))
        {
            throw new InvalidDataException("The backup references a service that does not exist.");
        }

        if (snapshot.ProductSales.Any(sale => !productIds.Contains(sale.ProductId)))
        {
            throw new InvalidDataException("The backup references a product that does not exist.");
        }

        if (snapshot.ProductSales.Any(sale => sale.WorkerId.HasValue && !workerIds.Contains(sale.WorkerId.Value)))
        {
            throw new InvalidDataException("The backup references a worker that does not exist.");
        }

        // Money and quantity sanity — mirrors the [Range] attributes that only
        // EditForm enforces, so bad values cannot arrive via import or old data.
        if (snapshot.Workers.Any(worker => worker.Name.Length == 0))
        {
            throw new InvalidDataException("The backup contains a worker without a name.");
        }

        if (snapshot.Workers.Any(worker => worker.DefaultCommissionPercentage is < 0 or > 100))
        {
            throw new InvalidDataException("The backup contains a commission percentage outside 0-100.");
        }

        if (snapshot.Services.Any(service => service.Name.Length == 0))
        {
            throw new InvalidDataException("The backup contains a service without a name.");
        }

        if (snapshot.Services.Any(service => service.BasePrice < 0))
        {
            throw new InvalidDataException("The backup contains a negative service price.");
        }

        if (snapshot.Products.Any(product => product.Name.Length == 0))
        {
            throw new InvalidDataException("The backup contains a product without a name.");
        }

        if (snapshot.Products.Any(product => product.Price < 0 || product.StockQuantity < 0))
        {
            throw new InvalidDataException("The backup contains a negative product price or stock quantity.");
        }

        if (snapshot.ServiceRecords.Any(record => record.AmountPaid < 0 || record.Tips < 0))
        {
            throw new InvalidDataException("The backup contains a negative amount or tip.");
        }

        if (snapshot.ServiceRecords.Any(record => record.CommissionPercentageApplied is < 0 or > 100))
        {
            throw new InvalidDataException("The backup contains a commission percentage outside 0-100.");
        }

        if (snapshot.ProductSales.Any(sale => sale.Quantity < 1 || sale.UnitPrice < 0))
        {
            throw new InvalidDataException("The backup contains a product sale with an invalid quantity or price.");
        }
    }

    private static void EnsureDistinctIds<T>(IEnumerable<T> items, Func<T, int> idSelector, string entityName)
    {
        var ids = items.Select(idSelector).ToList();

        if (ids.Any(id => id <= 0))
        {
            throw new InvalidDataException($"The backup contains invalid {entityName} identifiers.");
        }

        if (ids.Count != ids.Distinct().Count())
        {
            throw new InvalidDataException($"The backup contains duplicate {entityName} identifiers.");
        }
    }
}