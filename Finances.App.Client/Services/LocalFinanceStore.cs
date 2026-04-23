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

public sealed record DataStateSummary(int WorkerCount, int ServiceCount, int ProductCount, int ServiceRecordCount, int ProductSaleCount, int ClientCount, int RecurringServiceCount, int SchemaVersion, DateTime LastUpdatedUtc);

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

public sealed record GoalProgressResult(Goal Goal, decimal CurrentValue, decimal TargetValue, decimal ProgressPercent, string? WorkerName);

public sealed record ClientRetentionResult(string Period, int NewClients, int ReturningClients);

public sealed class LocalFinanceStore : IFinanceService
{
    private const int CurrentSchemaVersion = 5;
    private const string StorageKey = "Finances.App.snapshot";
    private const string BackupKeyPrefix = "Finances.App.backup.v";
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

    // ── Recurring Services ───────────────────────────────────

    public async Task<IReadOnlyList<RecurringService>> GetRecurringServicesAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.RecurringServices
            .OrderBy(r => r.NextOccurrence)
            .Select(CloneRecurring)
            .ToList();
    }

    public async Task<RecurringService> AddRecurringServiceAsync(RecurringService recurring)
    {
        ArgumentNullException.ThrowIfNull(recurring);
        await EnsureLoadedAsync();

        if (!_snapshot!.Workers.Any(w => w.Id == recurring.WorkerId))
            throw new InvalidDataException("The selected worker does not exist.");

        if (!_snapshot.Services.Any(s => s.Id == recurring.ServiceId))
            throw new InvalidDataException("The selected service does not exist.");

        if (recurring.ClientId.HasValue && !_snapshot.Clients.Any(c => c.Id == recurring.ClientId.Value))
            throw new InvalidDataException("The selected client does not exist.");

        var created = CloneRecurring(recurring);
        created.Id = _snapshot.NextRecurringServiceId++;
        created.Notes = NormalizeOptionalText(created.Notes);
        if (created.NextOccurrence == default) created.NextOccurrence = DateTime.Today;

        _snapshot.RecurringServices.Add(created);
        await PersistAsync();

        return CloneRecurring(created);
    }

    public async Task UpdateRecurringServiceAsync(int id, RecurringService recurring)
    {
        ArgumentNullException.ThrowIfNull(recurring);
        await EnsureLoadedAsync();

        if (id != recurring.Id)
            throw new InvalidDataException("Recurring service identifier mismatch.");

        var existing = _snapshot!.RecurringServices.FirstOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException();

        if (!_snapshot.Workers.Any(w => w.Id == recurring.WorkerId))
            throw new InvalidDataException("The selected worker does not exist.");

        if (!_snapshot.Services.Any(s => s.Id == recurring.ServiceId))
            throw new InvalidDataException("The selected service does not exist.");

        if (recurring.ClientId.HasValue && !_snapshot.Clients.Any(c => c.Id == recurring.ClientId.Value))
            throw new InvalidDataException("The selected client does not exist.");

        existing.WorkerId = recurring.WorkerId;
        existing.ServiceId = recurring.ServiceId;
        existing.ClientId = recurring.ClientId;
        existing.Frequency = recurring.Frequency;
        existing.NextOccurrence = recurring.NextOccurrence;
        existing.IsActive = recurring.IsActive;
        existing.Notes = NormalizeOptionalText(recurring.Notes);

        await PersistAsync();
    }

    public async Task DeleteRecurringServiceAsync(int id)
    {
        await EnsureLoadedAsync();
        var existing = _snapshot!.RecurringServices.FirstOrDefault(r => r.Id == id)
            ?? throw new KeyNotFoundException();
        _snapshot.RecurringServices.Remove(existing);
        await PersistAsync();
    }

    public async Task<IReadOnlyList<RecurringService>> GetDueRecurringServicesAsync()
    {
        await EnsureLoadedAsync();
        var today = DateTime.Today;
        return _snapshot!.RecurringServices
            .Where(r => r.IsActive && r.NextOccurrence.Date <= today)
            .OrderBy(r => r.NextOccurrence)
            .Select(EnrichRecurring)
            .ToList();
    }

    // ── Goals ────────────────────────────────────────────────

    public async Task<IReadOnlyList<Goal>> GetGoalsAsync()
    {
        await EnsureLoadedAsync();
        return _snapshot!.Goals.Select(CloneGoal).ToList();
    }

    public async Task<Goal> AddGoalAsync(Goal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        await EnsureLoadedAsync();

        if (goal.WorkerId.HasValue && !_snapshot!.Workers.Any(w => w.Id == goal.WorkerId.Value))
            throw new InvalidDataException("The selected worker does not exist.");

        var created = CloneGoal(goal);
        created.Id = _snapshot!.NextGoalId++;
        created.Label = NormalizeOptionalText(created.Label);

        _snapshot.Goals.Add(created);
        await PersistAsync();

        return CloneGoal(created);
    }

    public async Task UpdateGoalAsync(int id, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        await EnsureLoadedAsync();

        if (id != goal.Id)
            throw new InvalidDataException("Goal identifier mismatch.");

        var existing = _snapshot!.Goals.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException();

        if (goal.WorkerId.HasValue && !_snapshot.Workers.Any(w => w.Id == goal.WorkerId.Value))
            throw new InvalidDataException("The selected worker does not exist.");

        existing.Type = goal.Type;
        existing.TargetValue = goal.TargetValue;
        existing.Period = goal.Period;
        existing.WorkerId = goal.WorkerId;
        existing.Label = NormalizeOptionalText(goal.Label);
        existing.IsActive = goal.IsActive;

        await PersistAsync();
    }

    public async Task DeleteGoalAsync(int id)
    {
        await EnsureLoadedAsync();
        var existing = _snapshot!.Goals.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException();
        _snapshot.Goals.Remove(existing);
        await PersistAsync();
    }

    public async Task<IReadOnlyList<GoalProgressResult>> GetGoalProgressAsync(int? workerId)
    {
        await EnsureLoadedAsync();
        var today = DateTime.Today;
        var results = new List<GoalProgressResult>();

        foreach (var goal in _snapshot!.Goals.Where(g => g.IsActive))
        {
            // Skip goals for other workers when a worker filter is active
            if (workerId.HasValue && goal.WorkerId.HasValue && goal.WorkerId.Value != workerId.Value)
                continue;

            var (periodStart, periodEnd) = GetGoalPeriodDates(goal.Period, today);
            var effectiveWorkerId = goal.WorkerId ?? workerId;

            decimal current = goal.Type switch
            {
                GoalType.Revenue => FilterStoredRecords(effectiveWorkerId, periodStart, periodEnd).Sum(r => r.AmountPaid)
                                  + FilterStoredProductSales(effectiveWorkerId, periodStart, periodEnd).Sum(s => s.UnitPrice * s.Quantity),
                GoalType.ServiceCount => FilterStoredRecords(effectiveWorkerId, periodStart, periodEnd).Count(),
                GoalType.ProductSaleCount => FilterStoredProductSales(effectiveWorkerId, periodStart, periodEnd).Count(),
                _ => 0m
            };

            var pct = goal.TargetValue > 0 ? Math.Min(Math.Round(current / goal.TargetValue * 100, 1), 100m) : 0m;
            var workerName = goal.WorkerId.HasValue
                ? _snapshot.Workers.FirstOrDefault(w => w.Id == goal.WorkerId.Value)?.Name
                : null;

            results.Add(new GoalProgressResult(CloneGoal(goal), current, goal.TargetValue, pct, workerName));
        }

        return results;
    }

    private static (DateTime Start, DateTime End) GetGoalPeriodDates(GoalPeriod period, DateTime today)
    {
        return period switch
        {
            GoalPeriod.Weekly => (today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday), today),
            GoalPeriod.Monthly => (new DateTime(today.Year, today.Month, 1), today),
            _ => (new DateTime(today.Year, today.Month, 1), today)
        };
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

    public async Task<IReadOnlyList<ClientRetentionResult>> GetClientRetentionAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var records = FilterStoredRecords(workerId, from, to)
            .Where(r => r.ClientId.HasValue)
            .OrderBy(r => r.DatePerformed)
            .ToList();

        var sales = FilterStoredProductSales(workerId, from, to)
            .Where(s => s.ClientId.HasValue)
            .OrderBy(s => s.DateSold)
            .ToList();

        // Build a set of all client IDs seen before the period starts
        var allRecords = _snapshot!.ServiceRecords.Where(r => r.ClientId.HasValue).ToList();
        var allSales = _snapshot.ProductSales.Where(s => s.ClientId.HasValue).ToList();

        var periodStart = from ?? records.Select(r => (DateTime?)r.DatePerformed).FirstOrDefault() ?? DateTime.Today;
        var clientsSeenBefore = new HashSet<int>(
            allRecords.Where(r => r.DatePerformed.Date < periodStart.Date).Select(r => r.ClientId!.Value)
            .Concat(allSales.Where(s => s.DateSold.Date < periodStart.Date).Select(s => s.ClientId!.Value))
        );

        // Group interactions by month
        var interactions = records.Select(r => new { r.DatePerformed.Year, r.DatePerformed.Month, ClientId = r.ClientId!.Value })
            .Concat(sales.Select(s => new { s.DateSold.Year, s.DateSold.Month, ClientId = s.ClientId!.Value }))
            .GroupBy(x => new { x.Year, x.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .ToList();

        var seenSoFar = new HashSet<int>(clientsSeenBefore);
        var results = new List<ClientRetentionResult>();

        foreach (var group in interactions)
        {
            var clientsThisPeriod = group.Select(x => x.ClientId).Distinct().ToList();
            int newCount = 0, returningCount = 0;

            foreach (var cid in clientsThisPeriod)
            {
                if (seenSoFar.Contains(cid))
                    returningCount++;
                else
                {
                    newCount++;
                    seenSoFar.Add(cid);
                }
            }

            var label = new DateTime(group.Key.Year, group.Key.Month, 1).ToString("MMM yyyy");
            results.Add(new ClientRetentionResult(label, newCount, returningCount));
        }

        return results;
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
            _snapshot.RecurringServices.Count(r => r.IsActive),
            _snapshot.SchemaVersion,
            _snapshot.LastUpdatedUtc);
    }

    // ---- Client Comments ---------------------------------------------------

    public async Task<IReadOnlyList<ClientComment>> GetClientCommentsAsync(int clientId)
    {
        await EnsureLoadedAsync();
        return _snapshot!.ClientComments
            .Where(c => c.ClientId == clientId)
            .OrderByDescending(c => c.CreatedUtc)
            .ThenByDescending(c => c.Id)
            .Select(CloneClientComment)
            .ToList();
    }

    public async Task<ClientComment> AddClientCommentAsync(ClientComment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        await EnsureLoadedAsync();

        if (_snapshot!.Clients.All(c => c.Id != comment.ClientId))
        {
            throw new InvalidDataException("The referenced client does not exist.");
        }

        var stored = new ClientComment
        {
            Id = _snapshot.NextClientCommentId++,
            ClientId = comment.ClientId,
            Body = (comment.Body ?? string.Empty).Trim(),
            CreatedUtc = comment.CreatedUtc == default ? DateTime.UtcNow : comment.CreatedUtc,
            ReminderUtc = comment.ReminderUtc,
            ReminderHandled = comment.ReminderHandled
        };

        if (stored.Body.Length == 0)
        {
            throw new InvalidDataException("Comment body cannot be empty.");
        }

        _snapshot.ClientComments.Add(stored);
        await PersistAsync();
        return CloneClientComment(stored);
    }

    public async Task UpdateClientCommentAsync(int id, ClientComment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        await EnsureLoadedAsync();

        if (id != comment.Id)
        {
            throw new InvalidDataException("Comment identifier mismatch.");
        }

        var existing = _snapshot!.ClientComments.FirstOrDefault(c => c.Id == id)
            ?? throw new KeyNotFoundException();

        existing.Body = (comment.Body ?? string.Empty).Trim();
        if (existing.Body.Length == 0)
        {
            throw new InvalidDataException("Comment body cannot be empty.");
        }

        existing.ReminderUtc = comment.ReminderUtc;
        existing.ReminderHandled = comment.ReminderHandled;

        await PersistAsync();
    }

    public async Task DeleteClientCommentAsync(int id)
    {
        await EnsureLoadedAsync();
        var comment = _snapshot!.ClientComments.FirstOrDefault(c => c.Id == id)
            ?? throw new KeyNotFoundException();
        _snapshot.ClientComments.Remove(comment);
        await PersistAsync();
    }

    public async Task<IReadOnlyList<ClientComment>> GetDueClientCommentRemindersAsync()
    {
        await EnsureLoadedAsync();
        var now = DateTime.UtcNow;
        return _snapshot!.ClientComments
            .Where(c => !c.ReminderHandled && c.ReminderUtc is { } reminder && reminder <= now)
            .OrderBy(c => c.ReminderUtc)
            .Select(c =>
            {
                var clone = CloneClientComment(c);
                clone.Client = _snapshot.Clients
                    .Where(cl => cl.Id == c.ClientId)
                    .Select(CloneClient)
                    .FirstOrDefault();
                return clone;
            })
            .ToList();
    }

    public async Task MarkCommentReminderHandledAsync(int id)
    {
        await EnsureLoadedAsync();
        var comment = _snapshot!.ClientComments.FirstOrDefault(c => c.Id == id)
            ?? throw new KeyNotFoundException();
        comment.ReminderHandled = true;
        await PersistAsync();
    }

    // ---- Expenses ----------------------------------------------------------

    public async Task<IReadOnlyList<Expense>> GetExpensesAsync(DateTime? from = null, DateTime? to = null, ExpenseCategory? category = null)
    {
        await EnsureLoadedAsync();
        var query = _snapshot!.Expenses.AsEnumerable();

        if (from.HasValue) query = query.Where(e => e.Date.Date >= from.Value.Date);
        if (to.HasValue) query = query.Where(e => e.Date.Date <= to.Value.Date);
        if (category.HasValue) query = query.Where(e => e.Category == category.Value);

        return query
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Select(CloneExpense)
            .ToList();
    }

    public async Task<Expense> AddExpenseAsync(Expense expense)
    {
        ArgumentNullException.ThrowIfNull(expense);
        await EnsureLoadedAsync();

        var stored = CloneExpense(expense);
        stored.Id = _snapshot!.NextExpenseId++;
        stored.Date = stored.Date == default ? DateTime.Today : stored.Date.Date;
        stored.Vendor = NormalizeOptionalText(stored.Vendor);
        stored.Notes = NormalizeOptionalText(stored.Notes);
        stored.AttachmentId = NormalizeOptionalText(stored.AttachmentId);

        if (stored.InvoiceId.HasValue && _snapshot.Invoices.All(i => i.Id != stored.InvoiceId.Value))
        {
            throw new InvalidDataException("The referenced invoice does not exist.");
        }

        _snapshot.Expenses.Add(stored);
        await PersistAsync();
        return CloneExpense(stored);
    }

    public async Task UpdateExpenseAsync(int id, Expense expense)
    {
        ArgumentNullException.ThrowIfNull(expense);
        await EnsureLoadedAsync();

        if (id != expense.Id)
        {
            throw new InvalidDataException("Expense identifier mismatch.");
        }

        var existing = _snapshot!.Expenses.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException();

        existing.Date = expense.Date == default ? DateTime.Today : expense.Date.Date;
        existing.Category = expense.Category;
        existing.Amount = expense.Amount;
        existing.Vendor = NormalizeOptionalText(expense.Vendor);
        existing.Notes = NormalizeOptionalText(expense.Notes);
        existing.AttachmentId = NormalizeOptionalText(expense.AttachmentId);
        existing.InvoiceId = expense.InvoiceId;

        await PersistAsync();
    }

    public async Task DeleteExpenseAsync(int id)
    {
        await EnsureLoadedAsync();
        var expense = _snapshot!.Expenses.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException();
        _snapshot.Expenses.Remove(expense);
        await PersistAsync();
    }

    public async Task<NetProfitResult> GetNetProfitAsync(int? workerId, DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var services = FilterStoredRecords(workerId, from, to).Sum(r => r.AmountPaid);
        var productRevenue = FilterStoredProductSales(workerId, from, to).Sum(s => s.Quantity * s.UnitPrice);
        var revenue = services + productRevenue;

        var expensesQuery = _snapshot!.Expenses.AsEnumerable();
        if (from.HasValue) expensesQuery = expensesQuery.Where(e => e.Date.Date >= from.Value.Date);
        if (to.HasValue) expensesQuery = expensesQuery.Where(e => e.Date.Date <= to.Value.Date);
        var expenses = expensesQuery.Sum(e => e.Amount);

        return new NetProfitResult(revenue, expenses, revenue - expenses);
    }

    public async Task<IReadOnlyList<MonthlyProfitPoint>> GetMonthlyProfitSeriesAsync(DateTime? from, DateTime? to)
    {
        await EnsureLoadedAsync();

        var effectiveFrom = from ?? DateTime.Today.AddMonths(-11);
        var effectiveTo = to ?? DateTime.Today;

        var services = _snapshot!.ServiceRecords
            .Where(r => r.DatePerformed.Date >= effectiveFrom.Date && r.DatePerformed.Date <= effectiveTo.Date)
            .GroupBy(r => new { r.DatePerformed.Year, r.DatePerformed.Month })
            .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPaid));

        var sales = _snapshot.ProductSales
            .Where(s => s.DateSold.Date >= effectiveFrom.Date && s.DateSold.Date <= effectiveTo.Date)
            .GroupBy(s => new { s.DateSold.Year, s.DateSold.Month })
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity * s.UnitPrice));

        var expenses = _snapshot.Expenses
            .Where(e => e.Date.Date >= effectiveFrom.Date && e.Date.Date <= effectiveTo.Date)
            .GroupBy(e => new { e.Date.Year, e.Date.Month })
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var keys = services.Keys
            .Concat(sales.Keys)
            .Concat(expenses.Keys)
            .Distinct()
            .OrderBy(k => k.Year)
            .ThenBy(k => k.Month)
            .ToList();

        var results = new List<MonthlyProfitPoint>();
        foreach (var key in keys)
        {
            var label = new DateTime(key.Year, key.Month, 1).ToString("yyyy-MM");
            var revenue = (services.GetValueOrDefault(key, 0m)) + (sales.GetValueOrDefault(key, 0m));
            results.Add(new MonthlyProfitPoint(label, revenue, expenses.GetValueOrDefault(key, 0m)));
        }

        return results;
    }

    // ---- Invoices ----------------------------------------------------------

    public async Task<IReadOnlyList<Invoice>> GetInvoicesAsync(DateTime? from = null, DateTime? to = null, bool? paid = null, string? vendorSearch = null)
    {
        await EnsureLoadedAsync();
        var query = _snapshot!.Invoices.AsEnumerable();

        if (from.HasValue) query = query.Where(i => i.Date.Date >= from.Value.Date);
        if (to.HasValue) query = query.Where(i => i.Date.Date <= to.Value.Date);
        if (paid.HasValue) query = query.Where(i => i.Paid == paid.Value);
        if (!string.IsNullOrWhiteSpace(vendorSearch))
        {
            var needle = vendorSearch.Trim();
            query = query.Where(i => i.Vendor != null && i.Vendor.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        return query
            .OrderByDescending(i => i.Date)
            .ThenByDescending(i => i.Id)
            .Select(CloneInvoice)
            .ToList();
    }

    public async Task<Invoice?> GetInvoiceAsync(int id)
    {
        await EnsureLoadedAsync();
        var invoice = _snapshot!.Invoices.FirstOrDefault(i => i.Id == id);
        return invoice is null ? null : CloneInvoice(invoice);
    }

    public async Task<Invoice> AddInvoiceAsync(Invoice invoice, IEnumerable<InvoiceLineItem>? lineItems = null, bool trackAsExpense = false)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        await EnsureLoadedAsync();

        var stored = CloneInvoice(invoice);
        stored.Id = _snapshot!.NextInvoiceId++;
        stored.Date = stored.Date == default ? DateTime.Today : stored.Date.Date;
        stored.Vendor = NormalizeOptionalText(stored.Vendor);
        stored.Notes = NormalizeOptionalText(stored.Notes);
        stored.AttachmentId = NormalizeOptionalText(stored.AttachmentId);
        stored.Currency = string.IsNullOrWhiteSpace(stored.Currency) ? "PLN" : stored.Currency.Trim();

        _snapshot.Invoices.Add(stored);

        if (lineItems is not null)
        {
            foreach (var line in lineItems)
            {
                var storedLine = CloneInvoiceLineItem(line);
                storedLine.Id = _snapshot.NextInvoiceLineItemId++;
                storedLine.InvoiceId = stored.Id;
                storedLine.Description = NormalizeOptionalText(storedLine.Description);
                if (storedLine.Quantity == 0m) storedLine.Quantity = 1m;
                if (storedLine.LineTotal == 0m) storedLine.LineTotal = storedLine.Quantity * storedLine.UnitPrice;
                _snapshot.InvoiceLineItems.Add(storedLine);
            }
        }

        if (trackAsExpense)
        {
            var expense = new Expense
            {
                Id = _snapshot.NextExpenseId++,
                Date = stored.Date,
                Category = ExpenseCategory.Other,
                Amount = stored.Total,
                Vendor = stored.Vendor,
                Notes = stored.Notes,
                AttachmentId = stored.AttachmentId,
                InvoiceId = stored.Id
            };
            _snapshot.Expenses.Add(expense);
        }

        await PersistAsync();
        return CloneInvoice(stored);
    }

    public async Task UpdateInvoiceAsync(int id, Invoice invoice, IEnumerable<InvoiceLineItem>? lineItems = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        await EnsureLoadedAsync();

        if (id != invoice.Id)
        {
            throw new InvalidDataException("Invoice identifier mismatch.");
        }

        var existing = _snapshot!.Invoices.FirstOrDefault(i => i.Id == id) ?? throw new KeyNotFoundException();

        existing.Date = invoice.Date == default ? DateTime.Today : invoice.Date.Date;
        existing.Vendor = NormalizeOptionalText(invoice.Vendor);
        existing.Currency = string.IsNullOrWhiteSpace(invoice.Currency) ? "PLN" : invoice.Currency.Trim();
        existing.Total = invoice.Total;
        existing.Paid = invoice.Paid;
        existing.AttachmentId = NormalizeOptionalText(invoice.AttachmentId);
        existing.ParsedFromAttachment = invoice.ParsedFromAttachment;
        existing.Notes = NormalizeOptionalText(invoice.Notes);

        if (lineItems is not null)
        {
            _snapshot.InvoiceLineItems.RemoveAll(l => l.InvoiceId == id);
            foreach (var line in lineItems)
            {
                var storedLine = CloneInvoiceLineItem(line);
                storedLine.Id = _snapshot.NextInvoiceLineItemId++;
                storedLine.InvoiceId = id;
                storedLine.Description = NormalizeOptionalText(storedLine.Description);
                if (storedLine.Quantity == 0m) storedLine.Quantity = 1m;
                if (storedLine.LineTotal == 0m) storedLine.LineTotal = storedLine.Quantity * storedLine.UnitPrice;
                _snapshot.InvoiceLineItems.Add(storedLine);
            }
        }

        await PersistAsync();
    }

    public async Task DeleteInvoiceAsync(int id)
    {
        await EnsureLoadedAsync();
        var invoice = _snapshot!.Invoices.FirstOrDefault(i => i.Id == id) ?? throw new KeyNotFoundException();
        _snapshot.Invoices.Remove(invoice);
        _snapshot.InvoiceLineItems.RemoveAll(l => l.InvoiceId == id);
        foreach (var expense in _snapshot.Expenses.Where(e => e.InvoiceId == id))
        {
            expense.InvoiceId = null;
        }
        await PersistAsync();
    }

    public async Task<IReadOnlyList<InvoiceLineItem>> GetInvoiceLineItemsAsync(int invoiceId)
    {
        await EnsureLoadedAsync();
        return _snapshot!.InvoiceLineItems
            .Where(l => l.InvoiceId == invoiceId)
            .OrderBy(l => l.Id)
            .Select(CloneInvoiceLineItem)
            .ToList();
    }

    // ---- Attachments (metadata) -------------------------------------------

    public async Task<Attachment> RegisterAttachmentAsync(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (string.IsNullOrWhiteSpace(attachment.Id))
        {
            throw new InvalidDataException("Attachment id is required.");
        }

        await EnsureLoadedAsync();

        var existing = _snapshot!.Attachments.FirstOrDefault(a => a.Id == attachment.Id);
        if (existing is not null)
        {
            existing.Mime = attachment.Mime ?? existing.Mime;
            existing.OriginalFileName = NormalizeOptionalText(attachment.OriginalFileName);
            existing.SizeBytes = attachment.SizeBytes;
            existing.EntityType = NormalizeOptionalText(attachment.EntityType);
            existing.EntityId = attachment.EntityId;
            await PersistAsync();
            return CloneAttachment(existing);
        }

        var stored = CloneAttachment(attachment);
        stored.Id = attachment.Id.Trim();
        stored.Mime = string.IsNullOrWhiteSpace(stored.Mime) ? "application/octet-stream" : stored.Mime.Trim();
        stored.OriginalFileName = NormalizeOptionalText(stored.OriginalFileName);
        stored.EntityType = NormalizeOptionalText(stored.EntityType);
        if (stored.CreatedUtc == default) stored.CreatedUtc = DateTime.UtcNow;

        _snapshot.Attachments.Add(stored);
        await PersistAsync();
        return CloneAttachment(stored);
    }

    public async Task UnregisterAttachmentAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        await EnsureLoadedAsync();
        _snapshot!.Attachments.RemoveAll(a => a.Id == id);
        await PersistAsync();
    }

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string? entityType = null, int? entityId = null)
    {
        await EnsureLoadedAsync();
        var query = _snapshot!.Attachments.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => string.Equals(a.EntityType, entityType, StringComparison.OrdinalIgnoreCase));
        }
        if (entityId.HasValue)
        {
            query = query.Where(a => a.EntityId == entityId.Value);
        }
        return query
            .OrderByDescending(a => a.CreatedUtc)
            .Select(CloneAttachment)
            .ToList();
    }

    public async Task<Attachment?> GetAttachmentMetadataAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        await EnsureLoadedAsync();
        var attachment = _snapshot!.Attachments.FirstOrDefault(a => a.Id == id);
        return attachment is null ? null : CloneAttachment(attachment);
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

        if (imported.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"The selected backup is from a newer schema (v{imported.SchemaVersion}). Update the app before importing.");
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

        var incomingVersion = _snapshot.SchemaVersion;
        if (incomingVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"The stored data is from a newer schema (v{incomingVersion}). Update the app before continuing.");
        }

        if (incomingVersion < CurrentSchemaVersion && incomingVersion > 0)
        {
            await WriteBackupAsync(incomingVersion, json);
        }

        MigrateSnapshot(_snapshot);
        NormalizeSnapshot(_snapshot);

        if (incomingVersion < CurrentSchemaVersion)
        {
            await SaveAsync();
        }
    }

    private async Task WriteBackupAsync(int fromVersion, string json)
    {
        var key = $"{BackupKeyPrefix}{fromVersion}";
        try
        {
            var existing = await _js.InvokeAsync<string?>("localStorage.getItem", key);
            if (!string.IsNullOrEmpty(existing))
            {
                return;
            }
            await _js.InvokeVoidAsync("localStorage.setItem", key, json);
        }
        catch
        {
            // Backup is best-effort; quota/permission errors must not block migration.
        }
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

    private static RecurringService CloneRecurring(RecurringService r)
    {
        return new RecurringService
        {
            Id = r.Id,
            WorkerId = r.WorkerId,
            ServiceId = r.ServiceId,
            ClientId = r.ClientId,
            Frequency = r.Frequency,
            NextOccurrence = r.NextOccurrence,
            IsActive = r.IsActive,
            Notes = r.Notes
        };
    }

    private RecurringService EnrichRecurring(RecurringService r)
    {
        var clone = CloneRecurring(r);
        clone.Worker = _snapshot!.Workers.Where(w => w.Id == r.WorkerId).Select(CloneWorker).FirstOrDefault();
        clone.Service = _snapshot.Services.Where(s => s.Id == r.ServiceId).Select(CloneService).FirstOrDefault();
        clone.Client = r.ClientId.HasValue
            ? _snapshot.Clients.Where(c => c.Id == r.ClientId.Value).Select(CloneClient).FirstOrDefault()
            : null;
        return clone;
    }

    private static Goal CloneGoal(Goal g)
    {
        return new Goal
        {
            Id = g.Id,
            Type = g.Type,
            TargetValue = g.TargetValue,
            Period = g.Period,
            WorkerId = g.WorkerId,
            Label = g.Label,
            IsActive = g.IsActive
        };
    }

    private static ClientComment CloneClientComment(ClientComment c)
    {
        return new ClientComment
        {
            Id = c.Id,
            ClientId = c.ClientId,
            Body = c.Body,
            CreatedUtc = c.CreatedUtc,
            ReminderUtc = c.ReminderUtc,
            ReminderHandled = c.ReminderHandled
        };
    }

    private static Expense CloneExpense(Expense e)
    {
        return new Expense
        {
            Id = e.Id,
            Date = e.Date,
            Category = e.Category,
            Amount = e.Amount,
            Vendor = e.Vendor,
            Notes = e.Notes,
            AttachmentId = e.AttachmentId,
            InvoiceId = e.InvoiceId
        };
    }

    private static Invoice CloneInvoice(Invoice i)
    {
        return new Invoice
        {
            Id = i.Id,
            Date = i.Date,
            Vendor = i.Vendor,
            Currency = i.Currency,
            Total = i.Total,
            Paid = i.Paid,
            AttachmentId = i.AttachmentId,
            ParsedFromAttachment = i.ParsedFromAttachment,
            Notes = i.Notes
        };
    }

    private static InvoiceLineItem CloneInvoiceLineItem(InvoiceLineItem l)
    {
        return new InvoiceLineItem
        {
            Id = l.Id,
            InvoiceId = l.InvoiceId,
            Description = l.Description,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            LineTotal = l.LineTotal
        };
    }

    private static Attachment CloneAttachment(Attachment a)
    {
        return new Attachment
        {
            Id = a.Id,
            Mime = a.Mime,
            OriginalFileName = a.OriginalFileName,
            SizeBytes = a.SizeBytes,
            CreatedUtc = a.CreatedUtc,
            EntityType = a.EntityType,
            EntityId = a.EntityId
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

        snapshot.RecurringServices ??= [];

        foreach (var recurring in snapshot.RecurringServices)
        {
            recurring.Worker = null;
            recurring.Service = null;
            recurring.Client = null;
            recurring.Notes = NormalizeOptionalText(recurring.Notes);
            if (recurring.NextOccurrence == default) recurring.NextOccurrence = DateTime.Today;
        }

        snapshot.Goals ??= [];

        foreach (var goal in snapshot.Goals)
        {
            goal.Label = NormalizeOptionalText(goal.Label);
        }

        snapshot.ClientComments ??= [];

        foreach (var comment in snapshot.ClientComments)
        {
            comment.Client = null;
            comment.Body = (comment.Body ?? string.Empty).Trim();
            if (comment.CreatedUtc == default) comment.CreatedUtc = DateTime.UtcNow;
            if (comment.CreatedUtc.Kind == DateTimeKind.Unspecified)
                comment.CreatedUtc = DateTime.SpecifyKind(comment.CreatedUtc, DateTimeKind.Utc);
            if (comment.ReminderUtc is { } reminder && reminder.Kind == DateTimeKind.Unspecified)
                comment.ReminderUtc = DateTime.SpecifyKind(reminder, DateTimeKind.Utc);
        }

        snapshot.Expenses ??= [];

        foreach (var expense in snapshot.Expenses)
        {
            expense.Vendor = NormalizeOptionalText(expense.Vendor);
            expense.Notes = NormalizeOptionalText(expense.Notes);
            expense.AttachmentId = NormalizeOptionalText(expense.AttachmentId);
            if (expense.Date == default) expense.Date = DateTime.Today;
            else expense.Date = expense.Date.Date;
        }

        snapshot.Invoices ??= [];

        foreach (var invoice in snapshot.Invoices)
        {
            invoice.Vendor = NormalizeOptionalText(invoice.Vendor);
            invoice.Notes = NormalizeOptionalText(invoice.Notes);
            invoice.AttachmentId = NormalizeOptionalText(invoice.AttachmentId);
            invoice.Currency = string.IsNullOrWhiteSpace(invoice.Currency) ? "PLN" : invoice.Currency.Trim();
            if (invoice.Date == default) invoice.Date = DateTime.Today;
            else invoice.Date = invoice.Date.Date;
        }

        snapshot.InvoiceLineItems ??= [];

        foreach (var line in snapshot.InvoiceLineItems)
        {
            line.Description = NormalizeOptionalText(line.Description);
            if (line.Quantity == 0m) line.Quantity = 1m;
            if (line.LineTotal == 0m) line.LineTotal = line.Quantity * line.UnitPrice;
        }

        snapshot.Attachments ??= [];

        foreach (var attachment in snapshot.Attachments)
        {
            attachment.Id = (attachment.Id ?? string.Empty).Trim();
            attachment.Mime = string.IsNullOrWhiteSpace(attachment.Mime) ? "application/octet-stream" : attachment.Mime.Trim();
            attachment.OriginalFileName = NormalizeOptionalText(attachment.OriginalFileName);
            attachment.EntityType = NormalizeOptionalText(attachment.EntityType);
            if (attachment.CreatedUtc == default) attachment.CreatedUtc = DateTime.UtcNow;
        }

        snapshot.SchemaVersion = Math.Max(CurrentSchemaVersion, snapshot.SchemaVersion);
        snapshot.NextWorkerId = Math.Max(snapshot.NextWorkerId, snapshot.Workers.Select(worker => worker.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextServiceId = Math.Max(snapshot.NextServiceId, snapshot.Services.Select(service => service.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextProductId = Math.Max(snapshot.NextProductId, snapshot.Products.Select(product => product.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextServiceRecordId = Math.Max(snapshot.NextServiceRecordId, snapshot.ServiceRecords.Select(record => record.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextProductSaleId = Math.Max(snapshot.NextProductSaleId, snapshot.ProductSales.Select(sale => sale.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextClientId = Math.Max(snapshot.NextClientId, snapshot.Clients.Select(client => client.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextRecurringServiceId = Math.Max(snapshot.NextRecurringServiceId, snapshot.RecurringServices.Select(r => r.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextGoalId = Math.Max(snapshot.NextGoalId, snapshot.Goals.Select(g => g.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextClientCommentId = Math.Max(snapshot.NextClientCommentId, snapshot.ClientComments.Select(c => c.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextExpenseId = Math.Max(snapshot.NextExpenseId, snapshot.Expenses.Select(e => e.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextInvoiceId = Math.Max(snapshot.NextInvoiceId, snapshot.Invoices.Select(i => i.Id).DefaultIfEmpty().Max() + 1);
        snapshot.NextInvoiceLineItemId = Math.Max(snapshot.NextInvoiceLineItemId, snapshot.InvoiceLineItems.Select(l => l.Id).DefaultIfEmpty().Max() + 1);
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

        // v2 → v3: Add recurring services collection
        if (snapshot.SchemaVersion < 3)
        {
            snapshot.RecurringServices ??= [];
            snapshot.SchemaVersion = 3;
        }

        // v3 → v4: Add goals collection
        if (snapshot.SchemaVersion < 4)
        {
            snapshot.Goals ??= [];
            snapshot.SchemaVersion = 4;
        }

        // v4 → v5: Add client comments, expenses, invoices, invoice line items, attachments
        if (snapshot.SchemaVersion < 5)
        {
            snapshot.ClientComments ??= [];
            snapshot.Expenses ??= [];
            snapshot.Invoices ??= [];
            snapshot.InvoiceLineItems ??= [];
            snapshot.Attachments ??= [];
            if (snapshot.NextClientCommentId <= 0) snapshot.NextClientCommentId = 1;
            if (snapshot.NextExpenseId <= 0) snapshot.NextExpenseId = 1;
            if (snapshot.NextInvoiceId <= 0) snapshot.NextInvoiceId = 1;
            if (snapshot.NextInvoiceLineItemId <= 0) snapshot.NextInvoiceLineItemId = 1;
            snapshot.SchemaVersion = 5;
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
        EnsureDistinctIds(snapshot.RecurringServices, r => r.Id, "recurring services");
        EnsureDistinctIds(snapshot.Goals, g => g.Id, "goals");
        EnsureDistinctIds(snapshot.ClientComments, c => c.Id, "client comments");
        EnsureDistinctIds(snapshot.Expenses, e => e.Id, "expenses");
        EnsureDistinctIds(snapshot.Invoices, i => i.Id, "invoices");
        EnsureDistinctIds(snapshot.InvoiceLineItems, l => l.Id, "invoice line items");

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