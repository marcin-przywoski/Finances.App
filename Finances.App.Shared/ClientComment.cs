using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class ClientComment
{
    public int Id { get; set; }

    [Required]
    public int ClientId { get; set; }

    public SalonClient? Client { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional reminder date (UTC). When set and not yet handled, the reminder surfaces in
    /// the NotificationBell (and via the browser Notifications API when enabled).
    /// </summary>
    public DateTime? ReminderUtc { get; set; }

    public bool ReminderHandled { get; set; }
}
