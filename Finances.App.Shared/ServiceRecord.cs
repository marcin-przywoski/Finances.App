using System;
using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class ServiceRecord
{
    public int Id { get; set; }

    public int WorkerId { get; set; }
    public Worker? Worker { get; set; }

    public int ServiceId { get; set; }
    public Service? Service { get; set; }

    [Required(ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Required))]
    public DateTime DatePerformed { get; set; }

    [Range(0, 100000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Amount))]
    public decimal AmountPaid { get; set; }

    [Range(0, 100, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Percentage))]
    public decimal CommissionPercentageApplied { get; set; }

    [Range(0, 100000, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.Amount))]
    public decimal Tips { get; set; }

    [MaxLength(100, ErrorMessageResourceType = typeof(ValidationStrings), ErrorMessageResourceName = nameof(ValidationStrings.MaxLength))]
    public string? ClientName { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// The worker's cut, rounded to cents so displayed rows and their totals
    /// agree. SalonShare is the exact remainder, so the two always sum to
    /// AmountPaid.
    /// </summary>
    public decimal WorkerShare => Math.Round(AmountPaid * CommissionPercentageApplied / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal SalonShare => AmountPaid - WorkerShare;
}
