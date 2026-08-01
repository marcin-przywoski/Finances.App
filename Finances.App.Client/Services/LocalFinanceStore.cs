using System.Text.Json;
using Finances.App.Client.Models;
using Finances.App.Client.Services.Storage;
using Finances.App.Shared;

namespace Finances.App.Client.Services;

public enum SnapshotLoadStatus
{
    Ok,
    RecoveredFromCorruptData
}

public sealed partial class LocalFinanceStore : IFinanceStore, IAnalyticsService
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
            DefaultCommissionPercentage = worker.DefaultCommissionPercentage
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