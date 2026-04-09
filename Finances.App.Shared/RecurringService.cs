using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class RecurringService
{
    public int Id { get; set; }

    public int WorkerId { get; set; }
    public Worker? Worker { get; set; }

    public int ServiceId { get; set; }
    public Service? Service { get; set; }

    public int? ClientId { get; set; }
    public SalonClient? Client { get; set; }

    [Required]
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

    public DateTime NextOccurrence { get; set; } = DateTime.Today;

    public bool IsActive { get; set; } = true;

    [MaxLength(300)]
    public string? Notes { get; set; }
}

public enum RecurrenceFrequency
{
    Weekly,
    Biweekly,
    Monthly
}
