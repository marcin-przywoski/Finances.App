using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class InvoiceLineItem
{
    public int Id { get; set; }

    [Required]
    public int InvoiceId { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    [Range(0, 100000)]
    public decimal Quantity { get; set; } = 1m;

    [Range(0, 10_000_000)]
    public decimal UnitPrice { get; set; }

    [Range(0, 10_000_000)]
    public decimal LineTotal { get; set; }
}
