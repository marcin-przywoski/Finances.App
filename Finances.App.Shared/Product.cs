using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Product
{
    public int Id { get; set; }

    [Required(ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Required))]
    [MaxLength(150, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Amount))]
    public decimal Price { get; set; }

    [Range(0, 100000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Amount))]
    public int StockQuantity { get; set; }

    [MaxLength(50, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string? Category { get; set; }
}
