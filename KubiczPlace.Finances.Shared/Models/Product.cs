using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KubiczPlace.Finances.Shared.Models;

public class Product
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Precision(18,2)]
    public decimal PurchasePrice { get; set; }

    [Precision(18,2)]
    public decimal SellPrice { get; set; }

    public int QuantityInStock { get; set; }

    [Range(0,100)]
    public int DefaultSharePct { get; set; } = 10;

    // Navigation
    public ICollection<ProductSale> Sales { get; set; } = new List<ProductSale>();
}
