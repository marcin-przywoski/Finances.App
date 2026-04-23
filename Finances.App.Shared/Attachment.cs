using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

/// <summary>
/// Metadata about a binary attachment stored in IndexedDB under the <c>attachments</c> object store.
/// The actual bytes are never in the snapshot (size blowup would break localStorage); only the id here.
/// </summary>
public class Attachment
{
    [Required]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Mime { get; set; } = "application/octet-stream";

    [MaxLength(255)]
    public string? OriginalFileName { get; set; }

    public long SizeBytes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(32)]
    public string? EntityType { get; set; }

    public int? EntityId { get; set; }
}
