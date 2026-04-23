using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Invoice
{
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; } = DateTime.Today;

    [MaxLength(100)]
    public string? Vendor { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "PLN";

    [Range(0, 10_000_000)]
    public decimal Total { get; set; }

    public bool Paid { get; set; }

    [MaxLength(64)]
    public string? AttachmentId { get; set; }

    public bool ParsedFromAttachment { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
