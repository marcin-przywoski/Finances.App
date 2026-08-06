using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Expense
{
    public int Id { get; set; }

    public DateTime Date { get; set; }

    [Required(ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Required))]
    [MaxLength(50, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string Category { get; set; } = string.Empty;

    [Range(0, 100000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Amount))]
    public decimal Amount { get; set; }

    [MaxLength(500, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string? Note { get; set; }
}
