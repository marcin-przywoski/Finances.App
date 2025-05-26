using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace KubiczPlace.Finances.Shared.Models;

public class Invoice
{
    public int Id { get; set; }

    [Required]
    public string FileName { get; set; } = string.Empty;

    // Full PDF binary
    [Required]
    public byte[] Content { get; set; } = Array.Empty<byte>();

    public string? Number { get; set; }

    [Precision(18, 2)]
    public decimal? Amount { get; set; }

    public string? Payer { get; set; }

    public string? OcrText { get; set; }

    public DateTime UploadDate { get; set; } = DateTime.UtcNow;
}
