using System.Text.Json;
using Finances.App.Client.Models;
using Finances.App.Shared;
using Microsoft.JSInterop;

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

public sealed record DataStateSummary(int WorkerCount, int ServiceCount, int ProductCount, int ServiceRecordCount, int ProductSaleCount, int ClientCount, int SchemaVersion, DateTime LastUpdatedUtc);

public sealed record MonthComparisonResult(
    decimal CurrentRevenue, decimal PreviousRevenue,
    decimal CurrentTips, decimal PreviousTips,
    int CurrentCount, int PreviousCount,
    decimal CurrentAvgTicket, decimal PreviousAvgTicket);

public sealed record DayOfWeekResult(string Day, decimal Revenue, int Count);

public sealed record TopProductResult(string Product, int Quantity, decimal Revenue);

public sealed record CombinedTimelinePoint(string Date, decimal ServiceRevenue, decimal ProductRevenue);

public sealed record CombinedTimelineResult(CombinedTimelinePoint[] Points);

public sealed record ClientDetailResult(
    SalonClient Client,
    int TotalVisits,
    decimal TotalSpend,
    DateTime? LastVisit,
    IReadOnlyList<RecentRecordResult> RecentServices,
    IReadOnlyList<ClientProductSaleResult> RecentProductSales);

public sealed record ClientProductSaleResult(int Id, string Date, string Product, int Quantity, decimal Total, string? Worker);

public sealed record TopClientResult(string Client, int Visits, decimal Revenue);

public sealed class LocalFinanceStore : IFinanceService
{
    private const int CurrentSchemaVersion = 2;
    private const string StorageKey = "Finances.App.snapshot";
    private readonly IJSRuntime _js;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private FinanceSnapshot? _snapshot;

    public event Action? OnChange;

    public LocalFinanceStore(IJSRuntime js)
    {
        _js = js;
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

        if (_snapshot.ProductSales.Any(sale => sale.WorkerId == id))
        {
            return new DeleteResult(false, "Cannot delete worker with existing product sales.");
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

    public async Task<DeleteResult> DeleteProductAsync(int id)
    {
        await EnsureLoadedAsync();

        var product = _snapshot!.Products.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();

        if (_snapshot.ProductSales.Any(sale => sale.ProductId == id))
        {
            return new DeleteResult(false, "Cannot delete a product that already has recorded sales.");
        }

        _snapshot.Products.Remove(product);
        await PersistAsync();
        return new DeleteResult(true);
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

        if (sale.Quantity <= 0)
        {
            throw new InvalidDataException("Quantity must be at least 1.");
        }

        var product = _snapshot!.Products.FirstOrDefault(item => item.Id == sale.ProductId)
            ?? throw new InvalidDataException("The selected product does not exist.");

        if (sale.WorkerId.HasValue && _snapshot.Workers.All(worker => worker.Id != sale.WorkerId.Value))
        {
            throw new InvalidDataException("The selected worker does not exist.");
        }

        if (sale.Quantity > product.StockQuantity)
        {
            throw new InvalidDataException($"Only {product.StockQuantity} unit(s) of {product.Name} are currently in stock.");
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
            ClientId = sale.ClientId,
            Notes = NormalizeOptionalText(sale.Notes)
        };

        // Auto-link client by name if no ClientId provided
        if (!stored.ClientId.HasValue && stored.ClientName is not null)
        {
            var client = _snapshot.Clients.FirstOrDefault(c =>
                string.Equals(c.Name.Trim(), stored.ClientName, StringComparison.CurrentCultureIgnoreCase));
            if (client is not null) stored.ClientId = client.Id;
        }

        _snapshot.ProductSales.Add(stored);
        product.StockQuantity -= stored.Quantity;

        await PersistAsync();
        return EnrichProductSale(stored);
    }

    public async Task UpdateProductSaleAsync(int id, ProductSale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        await EnsureLoadedAsync();

        if (sale.Quantity <= 0)
        {
            throw new InvalidDataException("Quantity must be at least 1.");
        }

        if (id != sale.Id)
        {
            throw new InvalidDataException("Sale identifier mismatch.");
        }

        var existing = _snapshot!.ProductSales.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        var currentProduct = _snapshot.Products.FirstOrDefault(product => product.Id == existing.ProductId)
            ?? throw new InvalidDataException("The selected product does not exist.");
        var nextProduct = _snapshot.Products.FirstOrDefault(product => product.Id == sale.ProductId)
            ?? throw new InvalidDataException("The selected product does not exist.");

        if (sale.WorkerId.HasValue && sale.WorkerId.Value != 0 && _snapshot.Workers.All(worker => worker.Id != sale.WorkerId.Value))
        {
            throw new InvalidDataException("The selected worker does not exist.");
        }

        var currentProductStock = currentProduct.StockQuantity;
        var nextProductStock = nextProduct.StockQuantity;

        currentProduct.StockQuantity += existing.Quantity;

        try
        {
            if (sale.Quantity > nextProduct.StockQuantity)
            {
                throw new InvalidDataException($"Only {nextProduct.StockQuantity} unit(s) of {nextProduct.Name} are currently in stock.");
            }

            nextProduct.StockQuantity -= sale.Quantity;

            existing.ProductId = sale.ProductId;
            existing.WorkerId = sale.WorkerId == 0 ? null : sale.WorkerId;
            existing.DateSold = sale.DateSold == default ? DateTime.Today : sale.DateSold.Date;
            existing.Quantity = sale.Quantity;
            existing.UnitPrice = sale.UnitPrice;
            existing.ClientName = NormalizeOptionalText(sale.ClientName);
            existing.ClientId = sale.ClientId;
            existing.Notes = NormalizeOptionalText(sale.Notes);

            // Auto-link client by name if no ClientId provided
            if (!existing.ClientId.HasValue && existing.ClientName is not null)
            {
                var client = _snapshot.Clients.FirstOrDefault(c =>
                    string.Equals(c.Name.Trim(), existing.ClientName, StringComparison.CurrentCultureIgnoreCase));
                if (client is not null) existing.ClientId = client.Id;
            }

            await PersistAsync();
        }
        catch
        {
            currentProduct.StockQuantity = currentProductStock;
            nextProduct.StockQuantity = nextProductStock;
            throw;
        }
    }

    public async Task DeleteProductSaleAsync(int id)
    {
        await EnsureLoadedAsync();

        var sale = _snapshot!.ProductSales.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException();
        var product = _snapshot.Products.FirstOrDefault(item => item.Id == sale.ProductId);

        if (product is not null)
        {
            product.StockQuantity += sale.Quantity;
        }

        _snapshot.ProductSales.Remove(sale);
        await PersistAsync();
    }

    // ── Clients ──────────────────────────────────────────────

    public async Task<IReadOnlyList<SalonClient>> GetClientsAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.Clients.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(CloneClient).ToList();
    }

    public async Task<SalonClient> AddClientAsync(SalonClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        await EnsureLoadedAsync();

        var created = CloneClient(client);
        created.Id = _snapshot!.NextClientId++;
        created.Name = created.Name.Trim();
        created.Phone = NormalizeOptionalText(created.Phone);
        created.Email = NormalizeOptionalText(created.Email);
        created.Notes = NormalizeOptionalText(created.Notes);
        if (created.CreatedDate == default) created.CreatedDate = DateTime.Today;

        _snapshot.Clients.Add(created);
        await PersistAsync();

        return CloneClient(created);
    }

    public async Task UpdateClientAsync(int id, SalonClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        await EnsureLoadedAsync();

        if (id != client.Id) throw new InvalidDataException("Client identifier mismatch.");

        var existing = _snapshot!.Clients.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException();
        existing.Name = client.Name.Trim();
        existing.Phone = NormalizeOptionalText(client.Phone);
        existing.Email = NormalizeOptionalText(client.Email);
        existing.Notes = NormalizeOptionalText(client.Notes);

        await PersistAsync();
    }

    public async Task<DeleteResult> DeleteClientAsync(int id)
    {
        await EnsureLoadedAsync();

        var client = _snapshot!.Clients.FirstOrDefault(c => c.Id == id);
        if (client is null) throw new KeyNotFoundException();

        // Unlink records but don't delete them
        foreach (var r in _snapshot.ServiceRecords.Where(r => r.ClientId == id))
        {
            r.ClientId = null;
            r.Client = null;
        }
        foreach (var s in _snapshot.ProductSales.Where(s => s.ClientId == id))
        {
            s.ClientId = null;
            s.Client = null;
        }

        _snapshot.Clients.Remove(client);
        await PersistAsync();
        return new DeleteResult(true);
    }

    public async Task<SalonClient?> GetOrCreateClientByNameAsync(string? name)
    {
        var normalized = NormalizeOptionalText(name);
        if (normalized is null) return null;

        await EnsureLoadedAsync();

        var existing = _snapshot!.Clients.FirstOrDefault(c =>
            string.Equals(c.Name.Trim(), normalized, StringComparison.CurrentCultureIgnoreCase));

        if (existing is not null) return CloneClient(existing);

        var created = new SalonClient
        {
            Id = _snapshot.NextClientId++,
            Name = normalized,
            CreatedDate = DateTime.Today
        };
        _snapshot.Clients.Add(created);
        await PersistAsync();

        return CloneClient(created);
    }

    public async Task<ClientDetailResult> GetClientDetailAsync(int clientId)
    {
        await EnsureLoadedAsync();

        var client = _snapshot!.Clients.FirstOrDefault(c => c.Id == clientId) ?? throw new KeyNotFoundException();

        var serviceRecords = _snapshot.ServiceRecords
            .Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.DatePerformed)
            .ToList();

        var productSales = _snapshot.ProductSales
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.DateSold)
            .ToList();

        var totalSpend = serviceRecords.Sum(r => r.AmountPaid + r.Tips)
                       + productSales.Sum(s => s.UnitPrice * s.Quantity);

        var lastServiceDate = serviceRecords.Select(r => (DateTime?)r.DatePerformed).FirstOrDefault();
        var lastSaleDate = productSales.Select(s => (DateTime?)s.DateSold).FirstOrDefault();
        DateTime? lastVisit = lastServiceDate.HasValue && lastSaleDate.HasValue
            ? (lastServiceDate.Value > lastSaleDate.Value ? lastServiceDate : lastSaleDate)
            : lastServiceDate ?? lastSaleDate;

        var recentServices = serviceRecords.Take(20).Select(r => new RecentRecordResult(
            r.Id,
            r.DatePerformed.ToString("yyyy-MM-dd"),
            _snapshot.Workers.FirstOrDefault(w => w.Id == r.WorkerId)?.Name ?? "Unknown",
            _snapshot.Services.FirstOrDefault(s => s.Id == r.ServiceId)?.Name ?? "Unknown",
            r.AmountPaid,
            r.Tips,
            r.ClientName
        )).ToList();

        var recentSales = productSales.Take(20).Select(s => new ClientProductSaleResult(
            s.Id,
            s.DateSold.ToString("yyyy-MM-dd"),
            _snapshot.Products.FirstOrDefault(p => p.Id == s.ProductId)?.Name ?? "Unknown",
            s.Quantity,
            s.UnitPrice * s.Quantity,
            s.WorkerId.HasValue ? _snapshot.Workers.FirstOrDefault(w => w.Id == s.WorkerId.Value)?.Name : null
        )).ToList();

        return new ClientDetailResult(
            CloneClient(client),
            serviceRecords.Count + productSales.Count,
            totalSpend,
            lastVisit,
            recentServices,
            recentSales);
    }

    public async Task<IReadOnlyList<TopClientResult>> GetTopClientsAsync(int? workerId, DateTime? from, DateTime? to, int count = 10)
    {
        await EnsureLoadedAsync();

        var serviceRecords = FilterStoredRecords(workerId, from, to)
            .Where(r => r.ClientId.HasValue)
            .GroupBy(r => r.ClientId!.Value)
            .Select(g => new { ClientId = g.Key, Visits = g.Count(), Revenue = g.Sum(r => r.AmountPaid) })
            .ToList();

        var productSales = FilterStoredProductSales(workerId, from, to)
            .Where(s => s.ClientId.HasValue)
            .GroupBy(s => s.ClientId!.Value)
            .Select(g => new { ClientId = g.Key, Visits = g.Count(), Revenue = g.Sum(s => s.UnitPrice * s.Quantity) })
            .ToList();

        var combined = serviceRecords.Concat(productSales)
            .GroupBy(x => x.ClientId)
            .Select(g =>
            {
                var name = _snapshot!.Clients.FirstOrDefault(c => c.Id == g.Key)?.Name ?? "Unknown";
                return new TopClientResult(name, g.Sum(x => x.Visits), g.Sum(x => x.Revenue));
            })
            .OrderByDescending(c => c.Revenue)
            .Take(count)
            .ToList();

        return combined;
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
            ClientId = record.ClientId,
            Notes = NormalizeOptionalText(record.Notes)
        };

        // Auto-link client by name if no ClientId provided
        if (!stored.ClientId.HasValue && stored.ClientName is not null)
        {
            var client = _snapshot.Clients.FirstOrDefault(c =>
                string.Equals(c.Name.Trim(), stored.ClientName, StringComparison.CurrentCultureIgnoreCase));
            if (client is not null) stored.ClientId = client.Id;
        }

        _snapshot.ServiceRecords.Add(stored);
        await PersistAsync();

        return EnrichRecord(stored);
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
        existing.ClientId = record.ClientId;
        existing.Notes = NormalizeOptionalText(record.Notes);

        // Auto-link client by name if no ClientId provided
        if (!existing.ClientId.HasValue && existing.ClientName is not null)
        {
            var client = _snapshot.Clients.FirstOrDefault(c =>
                string.Equals(c.Name.Trim(), existing.ClientName, StringComparison.CurrentCultureIgnoreCase));
            if (client is not null) existing.ClientId = client.Id;
        }

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

    public async Task<IReadOnlyList<WorkerRevenueResult>> GetRevenueByWorkerAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var workerNames = _snapshot!.Workers.ToDictionary(worker => worker.Id, worker => worker.Name);

        return FilterStoredRecords(workerId, from, to)
            .GroupBy(record => workerNames.GetValueOrDefault(record.WorkerId, "Unknown"))
            .Select(group => new WorkerRevenueResult(
                group.Key,
                group.Sum(record => record.AmountPaid),
                group.Sum(record => record.Tips)))
            .OrderByDescending(item => item.Revenue)
            .ToList();
    }

    public async Task<IReadOnlyList<ServicePopularityResult>> GetServicePopularityAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var serviceNames = _snapshot!.Services.ToDictionary(service => service.Id, service => service.Name);

        return FilterStoredRecords(workerId, from, to)
            .GroupBy(record => serviceNames.GetValueOrDefault(record.ServiceId, "Unknown"))
            .Select(group => new ServicePopularityResult(
                group.Key,
                group.Count(),
                group.Sum(record => record.AmountPaid)))
            .OrderByDescending(item => item.Count)
            .ToList();
    }

    public async Task<ForecastResult> GetForecastAsync(int? workerId, DateTime? from, DateTime? to, int forecastDays)
    {
        await EnsureLoadedAsync();

        var effectiveTo = to?.Date ?? DateTime.Today;
        var effectiveFrom = from?.Date ?? effectiveTo.AddDays(-90);

        var dailyRevenue = FilterStoredRecords(workerId, effectiveFrom, effectiveTo)
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

    public async Task<IReadOnlyList<RecentRecordResult>> GetRecentAsync(int? workerId, int count)
    {
        await EnsureLoadedAsync();

        var workerNames = _snapshot!.Workers.ToDictionary(worker => worker.Id, worker => worker.Name);
        var serviceNames = _snapshot.Services.ToDictionary(service => service.Id, service => service.Name);

        return FilterStoredRecords(workerId, null, null)
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

        var currentServiceRevenue = currentRecords.Sum(r => r.AmountPaid);
        var previousServiceRevenue = previousRecords.Sum(r => r.AmountPaid);
        var currentProductRevenue = FilterStoredProductSales(workerId, currentStart, today).Sum(sale => sale.UnitPrice * sale.Quantity);
        var previousProductRevenue = FilterStoredProductSales(workerId, previousStart, previousEnd).Sum(sale => sale.UnitPrice * sale.Quantity);
        var currentRevenue = currentServiceRevenue + currentProductRevenue;
        var previousRevenue = previousServiceRevenue + previousProductRevenue;
        var currentTips = currentRecords.Sum(r => r.Tips);
        var previousTips = previousRecords.Sum(r => r.Tips);
        var currentCount = currentRecords.Count;
        var previousCount = previousRecords.Count;
        var currentAvgTicket = currentCount > 0 ? currentServiceRevenue / currentCount : 0;
        var previousAvgTicket = previousCount > 0 ? previousServiceRevenue / previousCount : 0;

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

    public async Task<IReadOnlyList<TopProductResult>> GetTopProductsAsync(int? workerId, DateTime? from, DateTime? to, int count = 5)
    {
        await EnsureLoadedAsync();

        var productNames = _snapshot!.Products.ToDictionary(p => p.Id, p => p.Name);
        var query = FilterStoredProductSales(workerId, from, to);

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

        var productQuery = FilterStoredProductSales(workerId, from, to);
        var productsByDay = productQuery
            .GroupBy(s => s.DateSold.Date)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.UnitPrice * s.Quantity));

        var allDates = servicesByDay.Keys.Union(productsByDay.Keys).OrderBy(d => d).ToList();

        var points = allDates.Select(d => new CombinedTimelinePoint(
            d.ToString("yyyy-MM-dd"),
            servicesByDay.GetValueOrDefault(d, 0),
            productsByDay.GetValueOrDefault(d, 0)
        )).ToArray();

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
            _snapshot.Clients.Count,
            _snapshot.SchemaVersion,
            _snapshot.LastUpdatedUtc);
    }

    public async Task<string> ExportAsync()
    {
        await EnsureLoadedAsync();
        return JsonSerializer.Serialize(_snapshot, _jsonOptions);
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
            imported = JsonSerializer.Deserialize<FinanceSnapshot>(json, _jsonOptions);
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

        _snapshot = imported;
        _snapshot.LastUpdatedUtc = DateTime.UtcNow;

        await SaveAsync();
        NotifyChanged();
    }

    public async Task ResetAsync()
    {
        _snapshot = CreateDefaultSnapshot();
        await SaveAsync();
        NotifyChanged();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_snapshot is not null)
        {
            return;
        }

        var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            _snapshot = CreateDefaultSnapshot();
            await SaveAsync();
            return;
        }

        try
        {
            _snapshot = JsonSerializer.Deserialize<FinanceSnapshot>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            _snapshot = null;
        }

        if (_snapshot is null)
        {
            _snapshot = CreateDefaultSnapshot();
            await SaveAsync();
            return;
        }

        MigrateSnapshot(_snapshot);
        NormalizeSnapshot(_snapshot);
    }

    private async Task PersistAsync()
    {
        _snapshot!.LastUpdatedUtc = DateTime.UtcNow;
        await SaveAsync();
        NotifyChanged();
    }

    private async Task SaveAsync()
    {
        _snapshot!.SchemaVersion = CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(_snapshot, _jsonOptions);
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
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

    private static SalonClient CloneClient(SalonClient client)
    {
        return new SalonClient
        {
            Id = client.Id,
            Name = client.Name,
            Phone = client.Phone,
            Email = client.Email,
            Notes = client.Notes,
            CreatedDate = client.CreatedDate
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
            ClientId = record.ClientId,
            Client = record.ClientId.HasValue
                ? _snapshot.Clients.Where(c => c.Id == record.ClientId.Value).Select(CloneClient).FirstOrDefault()
                : null,
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
            ClientId = sale.ClientId,
            Client = sale.ClientId.HasValue
                ? _snapshot.Clients.Where(c => c.Id == sale.ClientId.Value).Select(CloneClient).FirstOrDefault()
                : null,
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

    private IEnumerable<ProductSale> FilterStoredProductSales(int? workerId, DateTime? from, DateTime? to)
    {
        var query = _snapshot!.ProductSales.AsEnumerable();

        if (workerId.HasValue)
        {
            query = query.Where(sale => sale.WorkerId == workerId.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(sale => sale.DateSold.Date >= from.Value.Date);
        }

        if (to.HasValue)
        {
            query = query.Where(sale => sale.DateSold.Date <= to.Value.Date);
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
            worker.Name = worker.Name.Trim();
            worker.ApplicationUserId = NormalizeOptionalText(worker.ApplicationUserId);
        }

        foreach (var service in snapshot.Services)
        {
            service.Name = service.Name.Trim();
        }

        foreach (var product in snapshot.Products)
        {
            product.Name = product.Name.Trim();
            product.Category = NormalizeOptionalText(product.Category);
        }

        foreach (var record in snapshot.ServiceRecords)
        {
            record.Worker = null;
            record.Service = null;
            record.Client = null;
            record.ClientName = NormalizeOptionalText(record.ClientName);
            record.Notes = NormalizeOptionalText(record.Notes);
            record.DatePerformed = record.DatePerformed == default ? DateTime.Today : record.DatePerformed.Date;
        }

        snapshot.ProductSales ??= [];

        foreach (var sale in snapshot.ProductSales)
        {
            sale.Product = null;
            sale.Worker = null;
            sale.Client = null;
            sale.ClientName = NormalizeOptionalText(sale.ClientName);
            sale.Notes = NormalizeOptionalText(sale.Notes);
            sale.DateSold = sale.DateSold == default ? DateTime.Today : sale.DateSold.Date;
        }

        snapshot.Clients ??= [];

        foreach (var client in snapshot.Clients)
        {
            client.Name = client.Name.Trim();
            client.Phone = NormalizeOptionalText(client.Phone);
            client.Email = NormalizeOptionalText(client.Email);
            client.Notes = NormalizeOptionalText(client.Notes);
            if (client.CreatedDate == default) client.CreatedDate = DateTime.Today;
        }

        snapshot.SchemaVersion = Math.Max(CurrentSchemaVersion, snapshot.SchemaVersion);
        snapshot.NextWorkerId = Math.Max(snapshot.NextWorkerId, snapshot.Workers.Select(worker => worker.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextServiceId = Math.Max(snapshot.NextServiceId, snapshot.Services.Select(service => service.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextProductId = Math.Max(snapshot.NextProductId, snapshot.Products.Select(product => product.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextServiceRecordId = Math.Max(snapshot.NextServiceRecordId, snapshot.ServiceRecords.Select(record => record.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextProductSaleId = Math.Max(snapshot.NextProductSaleId, snapshot.ProductSales.Select(sale => sale.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextClientId = Math.Max(snapshot.NextClientId, snapshot.Clients.Select(client => client.Id).DefaultIfEmpty().Max() + 1);
        snapshot.LastUpdatedUtc = snapshot.LastUpdatedUtc == default ? DateTime.UtcNow : snapshot.LastUpdatedUtc;
    }

    private static void MigrateSnapshot(FinanceSnapshot snapshot)
    {
        if (snapshot.SchemaVersion <= 0)
        {
            snapshot.SchemaVersion = 1;
        }

        // v1 → v2: Create Client records from existing ClientName strings
        if (snapshot.SchemaVersion < 2)
        {
            snapshot.Clients ??= [];
            var nextClientId = snapshot.NextClientId > 0 ? snapshot.NextClientId : 1;
            var nameToClient = new Dictionary<string, SalonClient>(StringComparer.CurrentCultureIgnoreCase);

            foreach (var record in snapshot.ServiceRecords)
            {
                var name = NormalizeOptionalText(record.ClientName);
                if (name is null) continue;
                if (!nameToClient.TryGetValue(name, out var client))
                {
                    client = new SalonClient { Id = nextClientId++, Name = name, CreatedDate = record.DatePerformed.Date };
                    nameToClient[name] = client;
                    snapshot.Clients.Add(client);
                }
                record.ClientId = client.Id;
            }

            foreach (var sale in snapshot.ProductSales ?? [])
            {
                var name = NormalizeOptionalText(sale.ClientName);
                if (name is null) continue;
                if (!nameToClient.TryGetValue(name, out var client))
                {
                    client = new SalonClient { Id = nextClientId++, Name = name, CreatedDate = sale.DateSold.Date };
                    nameToClient[name] = client;
                    snapshot.Clients.Add(client);
                }
                sale.ClientId = client.Id;
            }

            snapshot.NextClientId = nextClientId;
            snapshot.SchemaVersion = 2;
        }

        if (snapshot.SchemaVersion < CurrentSchemaVersion)
        {
            snapshot.SchemaVersion = CurrentSchemaVersion;
        }
    }

    private static void ValidateSnapshot(FinanceSnapshot snapshot)
    {
        EnsureDistinctIds(snapshot.Workers, worker => worker.Id, "workers");
        EnsureDistinctIds(snapshot.Services, service => service.Id, "services");
        EnsureDistinctIds(snapshot.Products, product => product.Id, "products");
        EnsureDistinctIds(snapshot.ServiceRecords, record => record.Id, "service records");
        EnsureDistinctIds(snapshot.ProductSales, sale => sale.Id, "product sales");
        EnsureDistinctIds(snapshot.Clients, client => client.Id, "clients");

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