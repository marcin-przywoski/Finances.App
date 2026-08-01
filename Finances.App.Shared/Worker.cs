using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Worker
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100)]
    public decimal DefaultCommissionPercentage { get; set; }
}
