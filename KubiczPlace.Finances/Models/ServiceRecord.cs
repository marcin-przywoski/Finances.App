using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KubiczPlace.Finances.Models;

public class ServiceRecord
{
    public int Id { get; set; }

    [Required]
    public int WorkerId { get; set; }
    public Worker? Worker { get; set; }

    [Required]
    public int ServiceItemId { get; set; }
    public ServiceItem? ServiceItem { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    [Precision(18,2)]
    public decimal PricePaid { get; set; }

    [Range(0,100)]
    public int WorkerSharePct { get; set; } = 50;

    [NotMapped]
    public decimal WorkerEarnings => PricePaid * WorkerSharePct / 100m;

    [NotMapped]
    public decimal SalonEarnings => PricePaid - WorkerEarnings;
}
