using System.ComponentModel.DataAnnotations;

namespace KubiczPlace.Finances.Shared;

public class Service
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal BasePrice { get; set; }
}
