using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Worker
{
    public int Id { get; set; }

    [Required(ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Required))]
    [MaxLength(100, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Percentage))]
    public decimal DefaultCommissionPercentage { get; set; }
}
