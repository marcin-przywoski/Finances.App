using System.ComponentModel.DataAnnotations;

namespace KubiczPlace.Finances.Shared;

public class Product
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal Price { get; set; }

    [Range(0, 100000)]
    public int StockQuantity { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }
}
