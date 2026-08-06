using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Service
{
    public int Id { get; set; }

    [Required(ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Required))]
    [MaxLength(150, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Amount))]
    public decimal BasePrice { get; set; }
}
