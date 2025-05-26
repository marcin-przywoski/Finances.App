namespace KubiczPlace.Finances.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class Worker
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }

    [Range(0,100)]
    public int DefaultSharePct { get; set; } = 50;

    // Navigation
    public ICollection<ServiceRecord> ServiceRecords { get; set; } = new List<ServiceRecord>();
}
