using System;

namespace KubiczPlace.Finances.Shared;

public class ServiceRecord
{
    public int Id { get; set; }

    public int WorkerId { get; set; }
    public Worker? Worker { get; set; }

    public int ServiceId { get; set; }
    public Service? Service { get; set; }

    public DateTime DatePerformed { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal CommissionPercentageApplied { get; set; }
    public string? Notes { get; set; }

    // Calculated properties (logic to be implemented later)
    // public decimal WorkerShare => AmountPaid * (CommissionPercentageApplied / 100);
    // public decimal SalonShare => AmountPaid - WorkerShare;
}
