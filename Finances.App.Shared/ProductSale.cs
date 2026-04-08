using System;
using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class ProductSale
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int? WorkerId { get; set; }
    public Worker? Worker { get; set; }

    [Required]
    public DateTime DateSold { get; set; }

    [Range(1, 10000)]
    public int Quantity { get; set; } = 1;

    [Range(0, 100000)]
    public decimal UnitPrice { get; set; }

    public decimal TotalPrice => UnitPrice * Quantity;

    [MaxLength(100)]
    public string? ClientName { get; set; }

    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    public string? Notes { get; set; }
}
