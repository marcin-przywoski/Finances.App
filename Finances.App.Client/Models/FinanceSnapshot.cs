using Finances.App.Shared;

namespace Finances.App.Client.Models;

public sealed class FinanceSnapshot
{
    public int SchemaVersion { get; set; } = 4;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public int NextWorkerId { get; set; }
    public int NextServiceId { get; set; }
    public int NextProductId { get; set; }
    public int NextServiceRecordId { get; set; }
    public int NextProductSaleId { get; set; }
    public int NextClientId { get; set; }
    public int NextRecurringServiceId { get; set; }
    public int NextGoalId { get; set; }
    public List<Worker> Workers { get; set; } = [];
    public List<Service> Services { get; set; } = [];
    public List<Product> Products { get; set; } = [];
    public List<ServiceRecord> ServiceRecords { get; set; } = [];
    public List<ProductSale> ProductSales { get; set; } = [];
    public List<SalonClient> Clients { get; set; } = [];
    public List<RecurringService> RecurringServices { get; set; } = [];
    public List<Goal> Goals { get; set; } = [];
}