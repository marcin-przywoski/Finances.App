using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Expense
{
    public int Id { get; set; }

    public DateTime Date { get; set; }

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal Amount { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
