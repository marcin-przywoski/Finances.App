using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KubiczPlace.Finances.Shared.Models;

public class ProductSale
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int WorkerId { get; set; }
    public Worker? Worker { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    [Precision(18,2)]
    public decimal PriceSold { get; set; }

    [Range(0,100)]
    public int WorkerSharePct { get; set; } = 10;

    [NotMapped]
    public decimal WorkerEarnings => PriceSold * WorkerSharePct / 100m;

    [NotMapped]
    public decimal SalonEarnings => PriceSold - WorkerEarnings;
}
