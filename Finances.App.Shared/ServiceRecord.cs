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

    [Required]
    public DateTime DatePerformed { get; set; }

    [Range(0, 100000)]
    public decimal AmountPaid { get; set; }

    [Range(0, 100)]
    public decimal CommissionPercentageApplied { get; set; }

    [Range(0, 100000)]
    public decimal Tips { get; set; }

    [MaxLength(100)]
    public string? ClientName { get; set; }

    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    public string? Notes { get; set; }

    public decimal WorkerShare => AmountPaid * (CommissionPercentageApplied / 100);
    public decimal SalonShare => AmountPaid - WorkerShare;
}
