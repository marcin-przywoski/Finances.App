using KubiczPlace.Finances.Shared;

namespace KubiczPlace.Finances.Client.Models;

public sealed class FinanceSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public int NextWorkerId { get; set; }
    public int NextServiceId { get; set; }
    public int NextProductId { get; set; }
    public int NextServiceRecordId { get; set; }
    public List<Worker> Workers { get; set; } = [];
    public List<Service> Services { get; set; } = [];
    public List<Product> Products { get; set; } = [];
    public List<ServiceRecord> ServiceRecords { get; set; } = [];
}