using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Expense
{
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required]
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;

    [Range(0, 10_000_000)]
    public decimal Amount { get; set; }

    [MaxLength(100)]
    public string? Vendor { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>IndexedDB attachment id (string GUID) — may be null.</summary>
    [MaxLength(64)]
    public string? AttachmentId { get; set; }

    /// <summary>Optional link to an Invoice that generated this expense.</summary>
    public int? InvoiceId { get; set; }
}

public enum ExpenseCategory
{
    Rent,
    Utilities,
    Supplies,
    Marketing,
    Tax,
    Salary,
    Other
}
