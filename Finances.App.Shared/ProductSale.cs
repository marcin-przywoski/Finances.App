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

    [Required(ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Required))]
    public DateTime DateSold { get; set; }

    [Range(1, 10000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Quantity))]
    public int Quantity { get; set; } = 1;

    [Range(0, 100000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Amount))]
    public decimal UnitPrice { get; set; }

    public decimal TotalPrice => UnitPrice * Quantity;

    [MaxLength(100, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string? ClientName { get; set; }

    public string? Notes { get; set; }
}
